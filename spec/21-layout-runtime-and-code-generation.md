# 21. Layout, runtime metadata, and code generation

[Specification index](../SPEC.md)

Physical representation and code sharing must preserve Type identity, ownership, evaluation and cleanup. This chapter separates language-wide requirements, Windows storage contracts and compiler-controlled function passing.

The Windows storage and metadata contracts below are normative for this profile. They keep the capture and call restrictions of §7.6 and the compiler-controlled function passing of §21.4.2. Adoption in this specification does not establish implementation coverage (STATUS.md).

## 21.1. Structure layout and ABI

Storage layout, valid value representation and function ABI are separate contracts. A layout Attribute selects storage rules; it selects no calling convention, grants no Copy, and does not make a Type safe for foreign construction. The initial physical rules below belong to the versioned [Windows profile](#215-llvm-windows-x64-profile), not to a stable cross-version ABI.

### 21.1.1. Common layout rules

For a storable Type `T`, `size(T)` is the number of inline bytes it reserves, including padding; `alignment(T)` is a positive power of two; and `stride(T)` is the size rounded up to the alignment (zero when the size is zero). These are specification notation, not source operators. Rounding, addition and multiplication are checked for overflow. In windows-x64-v1, sizes, strides and valid Field offsets cannot exceed 2^63 − 1, and any stricter backend limit is diagnosed with its reason. A representable size does not guarantee successful allocation.

Fixed arrays have size and stride `N * stride(T)`, element offset `i * stride(T)` and alignment `alignment(T)`, including when `N = 0`. Unit has size 0, alignment 1 and stride 0. Zero-sized values keep their evaluation, initialization, ownership, Loans and destruction, and shared addresses do not merge logical Places. Positive-sized components cannot overlap, and nested padding cannot be reused by outer Fields. Padding has no guaranteed content, even in an initialized value; permitted byte transfers may include it, but typed reads, comparisons and integer coercions must not treat it as a value.

Layout uses the selected, merged storage declarations after Mods, Type substitution and storage classification. A struct contains its own Fields and its direct inline base, if any; computed members add no storage, and custom stored accessors do not change the slot count. Infinite inline storage cycles are rejected; borrows, raw pointers and object handles do not embed their referents. Reordering cannot change Field identity, logical initialization order, Partial Move, Loans or destruction order. The same compiler, target, settings and selected inputs must reproduce the layout.

### 21.1.2. Layout Attribute and fragments

`#Layout("Kimigayo")` and `#Layout("C")` are compiler-recognized Attributes on struct declarations only. They accept exactly one unnamed, non-interpolated string literal, matched case-sensitively; a trailing comma is allowed. Missing, extra and named arguments, unknown modes, wrong targets, and repeated Attributes on one fragment are diagnosed, even with equal values. No `#Repr` alternative or layout-query syntax is introduced.

An omitted Attribute means "unspecified" during fragment merging. The explicit mode is shared across the selected fragments: equal specifications on different fragments are allowed, and conflicting ones are errors. With no specification, Kimigayo layout is chosen; explicit Kimigayo and omission create no distinct Type identity.

C layout requires all selected instance Fields, including generated Fields, to occur in one fragment, in written order. Other fragments may add methods, computed members and other declarations without storage, and the Attribute need not be on the storage-bearing fragment. Kimigayo layout uses the logical order of §20.7.4. Neither mode uses filesystem enumeration or Binding order. Finalization follows Mods and selection, and storage cannot be appended afterward.

A generic struct shares its mode across instantiations, but each concrete Type has its own size, alignment, offsets and formation checks. Unresolved layout obligations are kept until the required instantiation; equal layout or code sharing is never inferred from equal modes.

```kimi
// Record.kimi
#Layout("C")
struct NativeRecord
    var kind: u8
    var value: u64
    var flags: u8

// Methods.kimi: shares C layout without repeating the Attribute.
struct NativeRecord
    func read(self: ref/Self) -> u64 => self.value

#Layout("c") // Error: mode names are case-sensitive.
struct Invalid
    var value: i32
```

### 21.1.3. Kimigayo and C modes

**Kimigayo mode.** Each Field and the base meet their natural alignment, and the aggregate alignment is at least their maximum; reserved component ranges fit inside the struct. The profile algorithm below fixes the layout. It promises no target-independent offsets, globally minimal size, C compatibility or ABI stability across compiler or input changes. This is Kimigayo's own contract, not rustc layout or the Rust ABI.

The Windows algorithm reserves the complete direct base at offset zero, then places the own Fields in **descending natural alignment**, breaking ties by logical order. Each component is aligned and the total rounded up to the maximum alignment, with checked arithmetic; size equals stride. Base and nested tail padding are never reused. If all components are zero-sized, size and stride are zero and the alignment is their maximum, or 1 when there are none.

The same alignment-sorted algorithm applies to concrete Closure captures, Tuple elements and each enum Case payload. It reorders neither C fields, array positions, the base nor an enum tag. Logical initialization, access identities, Partial Move and reverse destruction order stay unchanged at every optimization level. Generic offsets depend on the whole instantiated layout, including fields with fixed declared Types.

```text
Logical components: a: u8, b: u64, c: u8, d: u64
Physical order:     b @ 0, d @ 8, a @ 16, c @ 17
Size/stride: 24; alignment: 8 (rather than 32 in logical order).
Acquisition: a, b, c, d; destruction: d, c, b, a.
```

**C mode.** The initial contract is the Windows x64 MSVC layout with effective packing 16 (`/Zp16`), without `pragma pack`, explicit alignment or bit-fields; host packing settings are not inherited. Nested Types keep their own modes. Natural Field alignment must be at most 16; larger alignment is unsupported, never silently reduced.

```text
cursor = 0
aggregateAlignment = 1
for each Field F in written order:
    a = min(alignment(F.Type), 16)
    cursor = checkedAlignUp(cursor, a)
    offset(F) = cursor
    cursor = checkedAdd(cursor, size(F.Type))
    aggregateAlignment = max(aggregateAlignment, a)
alignment(S) = aggregateAlignment
size(S) = stride(S) = checkedAlignUp(cursor, aggregateAlignment)
```

C layout is rejected on open or derived structs, empty structs, direct zero-sized Fields (including Unit and zero-length arrays) and multiple storage-bearing fragments. Methods, constructors, computed members and `deinit` do not by themselves prevent C layout; foreign construction and destruction are a separate contract. C layout describes the owned payload, not an object handle, allocation header or reference count.

`NativeRecord` above has offsets 0, 8 and 16, size and stride 24, and alignment 8:

```llvm
%NativeRecord = type { i8, i64, i8 }
; In a function, with valid initialized storage:
%address = getelementptr %NativeRecord, ptr %record, i32 0, i32 1
%value = load i64, ptr %address, align 8
```

```c
#include <stdint.h>
#include <stddef.h>
typedef struct NativeRecord {
    uint8_t kind;
    uint64_t value;
    uint8_t flags;
} NativeRecord;
/* Compile with Windows x64 packing 16 and no pragma pack. */
_Static_assert(offsetof(NativeRecord, value) == 8, "value offset");
_Static_assert(sizeof(NativeRecord) == 24, "size");
_Static_assert(_Alignof(NativeRecord) == 8, "alignment");
```

C layout uses ordinary LLVM structs, not packed `<{ ... }>` structs; allocation size, ABI alignment and offsets are verified against TypeLayout. Source Field index, LLVM element index and byte offset are distinct. C mode alone defines neither native exports, mangling, DLL compatibility, serialization nor aggregate argument passing.

### 21.1.4. Initial scalar representation

windows-x64-v1 is little endian, uses address space 0, and has 64-bit pointers. This table is validated against LLVM 22.1.8's target DataLayout. LLVM integer Types carry width, not signedness; signed or unsigned operations are selected from the language Type.

| Language Type | LLVM storage | LLVM computation | Size / alignment / stride, bytes |
| --- | --- | --- | --- |
| `i8` / `u8` | `i8` | `i8` | 1 / 1 / 1 |
| `i16` / `u16` | `i16` | `i16` | 2 / 2 / 2 |
| `i32` / `u32` | `i32` | `i32` | 4 / 4 / 4 |
| `i64` / `u64`, `isize` / `usize` | `i64` | `i64` | 8 / 8 / 8 |
| `i128` / `u128` | `i128` | `i128` | 16 / 16 / 16 |
| `f32` / `f64` | `float` / `double` | `float` / `double` | 4 / 4 / 4; 8 / 8 / 8 |
| `bool` | `i8` | `i1` | 1 / 1 / 1 |
| `char` | `i32` | `i32` | 4 / 4 / 4 |
| `unsafe/T` | `ptr` | `ptr` | 8 / 8 / 8 |
| Unit | May be omitted | No ordinary value | 0 / 1 / 0 |

The only valid stored `bool` bytes are 0 and 1. Under that validity premise, a load reads `i8` and truncates to `i1`, and a store zero-extends `i1` to `i8`; this does not sanitize invalid bytes. Windows `BOOL` is `i32` and converts by comparison with zero. `char` stores an unsigned Unicode scalar value; surrogates and values above U+10FFFF are invalid. Neither representation establishes compatibility with C `char` or `WCHAR`.

This table does not authorize every operation or FFI use. Safe borrow and object-handle storage follows §21.2, and an equal pointer representation grants no raw-pointer permissions. Heap alignment support is separately limited to 16 (§22.5.2); a Type's alignment is never rounded down.

### 21.1.5. Tuple and enum layout

Tuples use the alignment-sorted aggregate algorithm of §21.1.3, keeping logical indices and evaluation and destruction order separate from physical offsets. All-zero-sized elements yield size and stride zero. Tuples have no Layout Attribute and no C ABI. For example, `(u8, u64)` has logical-element offsets 8 and 0, size and stride 16, and alignment 8.

Initial enums use an explicit `i32` tag and an aligned payload area, without niche optimization. Selected Cases are numbered from zero in declaration order; more than 2^32 Cases is unsupported. These internal tags introduce no source discriminants or integer conversions.

Each Case payload is laid out with the alignment-sorted algorithm of §21.1.3, without moving the enum tag. Let `A` be the maximum payload alignment and `P` the maximum payload size rounded up to `A`; if all payloads are empty, `A = 1` and `P = 0`. The payload starts at `alignUp(4, A)`, the enum alignment is `max(4, A)`, and the total size and stride are rounded up to that alignment. Only a selected tag paired with its valid active payload is a valid enum value. Initialization, Move, match and cleanup use that active Case, never unused payload bytes.

Payload alignment is preserved in LLVM with a zero-length alignment carrier followed by `P` bytes, using `[0 x i8/i16/i32/i64/i128]` for alignment 1/2/4/8/16. The carrier is neither a language Field nor a cleanup target.

```kimi
enum Message
    Quit
    Number(i64)
```

```llvm
; Payload offset 8, total size 16, alignment 8.
%MessagePayload = type { [0 x i64], [8 x i8] }
%Message = type { i32, %MessagePayload }
```

These layouts are verified when nested in structs and arrays. Payloads are never flattened to alignment-1 byte arrays, tag-only mutation is never exposed, and no C enum or union compatibility is claimed.

### 21.1.6. C exchange eligibility

C layout and C-exchangeable storage are different judgments. Initially, the eligible Types are owned `i8`/`u8` through `i64`/`u64`, owned `f32`/`f64`, raw `unsafe/T` pointers, positive-length fixed arrays of eligible elements, and owned C-layout structs whose Fields are recursively eligible.

Kimigayo-layout structs, Tuples, enums, `string`, dynamic collections, `bool`, `char`, `i128`/`u128`, `isize`/`usize`, safe borrows, object handles, and function and Closure values are excluded until their C correspondence is specified. A C-layout struct may contain a `string` if layout succeeds, but it is then not C-exchangeable. For example, C-layout `Pair<i32>` and `Pair<u64>` have different layouts, `Pair<()>` violates the zero-sized Field restriction, and `Pair<string>` gains no marshalling.

A pointer guarantees only its value representation; its pointee may remain opaque. Foreign reads and writes require a separate pointee-layout and validity contract. Foreign construction or overwriting of values received by Kimigayo initially also requires no user `deinit`, recursively. Constructors, lifetimes, ownership transfer, active Loans and accessor bypass still require the unsafe contract. No automatic Copy capability, raw-storage initialization API or safe-to-raw conversion follows.

For ABI comparison, the target ABI, recursively eligible Field Types, merged order and content, and layout options are kept. Aggregate arguments and results remain excluded from LibraryImport (§22.3), even when the storage is C-exchangeable. Packed and transparent layouts, explicit alignment and offsets, unions, bit-fields, flexible array members, external enum representations and public layout queries remain extensions.

## 21.2. Runtime representations and metadata

### 21.2.1. Type identity and descriptors

Every live object can reach immutable metadata for its Dynamic Type, shareable among objects of that concrete Type. The required logical information is:

```text
Type Descriptor
    ├─ Runtime Type Identity
    ├─ relationships required by defined Supports operations
    ├─ complete dynamic destruction operation
    └─ layout or receiver-adjustment information where required
```

**Runtime Object Type Identity** (here also Runtime Type Identity) identifies the actual concrete payload Core `D`. It uses these logical functions on Types whose necessary formation checks have succeeded:

```text
N(A)      = normalize transparent aliases, resolved associated-Type projections,
            grouping, and redundant owner prefixes
ArgKey(A) = remove every Origin{N}(A), recursively retaining Type structure
            and Semantics; nominal nodes retain Symbol/Kotonoha/version and
            their ordered argument keys
CoreId(D) = the concrete Core's identity computed by the same rules
```

An object handle's Runtime Type Identity is `CoreId(D)`, excluding its root `obj`/`rc`/`arc`/`objref`/`objuniq`. This root-handle removal is not applied recursively inside generic arguments, whose Object Semantics remain in the ArgKey. Function and Tuple structure and generated Closure declaration identity are preserved. A static base or Contract View Target does not substitute for the actual `D`.

| Comparison, assuming valid Types | Runtime Type Identity |
| --- | --- |
| `obj/D` and `rc/D` for the same actual `D` | Equal |
| `Box<ref{a}/i32>` and `Box<ref{b}/i32>` | Equal |
| `Box<ref/i32>` and `Box<i32>` | Different |
| `Box<objref/C>` and `Box<obj/C>` | Different: inner Semantics are retained |
| `obj/Box<ref{a}/i32>` and `obj/Box<i32>` | Different after root-handle removal |

Equal runtime identity implies neither equal value representation, layout, ABI, ownership operations, assignment compatibility, Origins nor Loans; it neither authorizes code sharing nor skips validation. Equal names, layouts, member sets or descriptor addresses alone do not define identity either. Fixed-array Type structure keeps the evaluated length and the element-Type key; general Const arguments beyond function lengths are not introduced. Other identity and key purposes are separated under [generation keys](#2132-identity-and-generation-keys).

Declared base and conformance relationships are fixed by validated definitions; no unrelated extension, module search or runtime registration changes them. Dependent artifacts are revalidated under §21.3.4 before metadata is generated, and their identities, bases and verified mappings must agree independently of load order. Receiver adjustment cannot bypass the public ObjectCallCompatible, access, Type, Origin or Loan checks. ObjectCallCompatible is compile-time interface information, not a required descriptor field. A destruction entry does not make `deinit` a source-level function value.

An extension that introduces runtime implementation selection defines its own additional selection information. The current descriptor has no runtime member-dispatch slots; its Windows storage is specified below, independently of the function ABI.

### 21.2.2. Value metadata and object descriptors

The following immutable, module-local records belong to windows-x64-v1. They define storage and entry contracts, not a stable external calling convention, and all physical signatures use the FunctionAbi of §21.4.2. Instance counts, allocation state, initialization state and Loans do not belong in shared metadata.

**Type keys.** Value metadata uses the ArgKey of the full normalized Type, recursively erasing only Origins and keeping all Semantics. Object payload metadata uses `CoreId(D)` of the complete Dynamic Type `D`, which equals `ArgKey(owner/D)` and is independent of the handle mode and static View Target. Distinct nonzero `u64` tokens are assigned deterministically within the final generation unit, and hash collisions are checked against the original keys. Artifacts keep those keys and dependencies, and integration may retokenize all references. Tokens have no public numeric, persistence or dynamic-linking contract. Several records for one key are allowed; shared code or layout never merges Type identities, and equal tokens prove no static Type, Origin, Loan or code-sharing judgment.

**ValueMetadata: 48 bytes, alignment 8.**

| Offset | Field | Contract |
| --- | --- | --- |
| 0 | `typeKey: u64` | Nonzero full-value Type token |
| 8 | `size: u64` | Inline size including tail padding; already aligned, hence also the stride |
| 16 | `alignment: u64` | Positive power of two, equal to the TypeLayout alignment |
| 24 | `flags: u64` | Bit 0 is HasCopy; all other bits are zero |
| 32 | `destroyValues: ptr` | Null exactly when no value of the full Type needs runtime destruction |
| 40 | `typeContext: ptr` | Immutable required Type information; null when unnecessary |

There is no separate stride or Copy/Move entry. All current Copy Types have no user `deinit` and recursively cleanup-free components, so HasCopy implies a null `destroyValues`; the converse is false. Zero size, or the current enum Case, does not prove the full Type cleanup-free. Never has no value metadata, although its static identity and constraints remain meaningful.

`destroyValues(first, count, metadata, location)` is the sole metadata destruction entry; a single value uses `count = 1`. For `count = 0` or a null entry, the call and its address calculation are skipped. Otherwise `first` is nonnull, aligned and live, pointing to `count` complete initialized values at `metadata.size` stride. The caller checks the count, range and offset arithmetic, and excludes moved, partial and spare storage. Values are destroyed in reverse logical order, `count - 1` down to zero; a zero stride still requires every logical destruction. Each value's `deinit` and component cleanup complete before the preceding value is destroyed. Abort or nontermination stops subsequent work. This entry destroys values but never frees the enclosing storage.

Partial cleanup may batch only contiguous complete segments compatible with the required logical order. Dictionary reverse-insertion value/key cleanup is not an address sort, so it uses separate calls, including `count = 1`, where needed. A range of strings can use one indirect entry and a reverse loop, without eliminating each value's required free or nested dynamic dispatch.

**TypeContext** supplies everything the entry needs independently of the original caller's stack or context: component and Case metadata, selected operation and specialization callees with their contexts, or equivalent information embedded in code. If a Type operation needs offsets, it provides a logical-element map derived from the **whole instantiated layout**, including Fields whose declared Types were already fixed; unused maps are omitted. Its reader and producer agree on the schema. It stores no instance length, partial state, Origin or Loan. A fixed-array TypeContext keeps `elementMetadata` and `N`. Ordinary shared-body layout and length reads use direct GenericContext slots (§21.3.3) derived from the same TypeLayout.

**ObjectDescriptor: 24 bytes, alignment 8.** It is shared across `obj`/`rc`/`arc` objects of the same complete Dynamic Type and layout.

| Offset | Field | Contract |
| --- | --- | --- |
| 0 | `payloadMetadata: ptr` | Nonnull ValueMetadata for the complete `D`; its `typeKey` is `CoreId(D)` |
| 8 | `freeStorage: ptr` | Nonnull operation that releases the original object allocation only |
| 16 | `viewMap: ptr` | Nullable verified Supports/base/receiver-adjustment information |

`viewMap` introduces no runtime method dispatch. The logical free entry is `freeStorage(header, descriptor, location)`; it performs no value destruction, count operation or side-table release. The fixed header of §21.2.3 puts the complete payload at `header + 16` without a descriptor load. The allocation size is the checked `16 + payloadMetadata.size`, constant when concrete and otherwise checked at runtime. Free needs no size argument. The descriptor duplicates neither the allocation size, the payload offset nor the allocation alignment; payload alignment stays in ValueMetadata. Metadata for `rc/D` describes a handle and cannot replace the metadata for payload `D`.

For a Sealed object View Target, the Dynamic Type equals that target. Whole payload updates preserve the descriptor, header, allocation and Identity, and a content-destruction operation such as `destroyValues` never frees or final-releases the containing object (§15.7.3).

Each view must reach the original identity, valid receiver and cast adjustments, and complete dynamic destruction and storage release, while preserving Origins and Loans. Descriptor addresses are not Type identity. Dynamic Array/Dictionary and Slice still require their own complete storage and element-state plans before emission; §4.7 does not prescribe their ABI. The value-borrow and callable layouts below are specified, with no implicit unsupported one-pointer fallback.

### 21.2.3. Windows x64 object and weak profile

#### 21.2.3.1. Storage and count representation

All allocations use the 16-byte-aligned allocator of §22.5.2. These are internal storage contracts, not FFI or cross-version ABI guarantees.

| Value or storage | Fields or pointer target | Size / alignment, bytes |
| --- | --- | --- |
| `obj`/`rc`/`arc`/`objref`/`objuniq` | Nonnull pointer to the original object header, unchanged by views | 8 / 8 |
| `Weak<S>` | Nonnull pointer to its side table | 8 / 8 |
| Every object header | +0 nonnull ObjectDescriptor pointer; +8 `u64` control (reserved zero for `obj`) | 16 / 8 |
| Side table | +0 `u64` strong; +8 `u64` weak; +16 immutable original object pointer | 24 / 8 |

The complete payload begins at `header + 16` in every mode. `16 + payload size` is checked against 2^63 − 1 and allocation limits, and allocation failure Aborts. Payload alignment above 16 is unsupported and diagnosed before generation. There is no over-allocation, private prefix or mode-dependent payload offset. The handle's static Semantics, not the descriptor, selects the counting behavior.

Every count is a `u64` with `MaxRefCount = 2^63 - 1`, including the internal weak guard. An increment at the maximum Aborts before updating; counts never wrap, and no migration happens merely to extend the maximum. An even counted-header control stores `strong << 1`; an odd control stores `sideTablePointer | 1`, and clearing only the low bit recovers the aligned address-space-0 pointer, preserving all high bits. The control of a normal unpublished object starts at zero and becomes 2 (strong = 1) when publication follows construction. A final zero is never reused.

The side-table strong count is 1..MaxRefCount while Alive, and zero both while Building and after final release. **There is no Building sentinel.** Only a factory holding unpublished construction authority may change zero to one, exactly once; observing zero grants no such authority. Cyclic construction starts with strong = 0 and weak = 2: one guard plus the builder's Weak. The complete lifecycle is not inferred solely from count bits.

#### 21.2.3.2. Migration, promotion and release

All callers share the internal contracts `ensureSideTable`, `retainStrong`, `tryRetainStrong`, `retainWeak`, `releaseStrong`, `releaseWeak` and `publishObject`; these names are not public APIs. Each access requires valid ownership, borrowing, or construction or destruction authority. There is exactly one authoritative strong-count location. `rc` is non-atomic, while `arc` operations and transitions are linearizable: published `arc` control, strong and weak accesses are atomic and never mixed with non-atomic accesses. Ordinary initialization is allowed only while storage is unpublished and inaccessible.

On the first downgrade, a valid strong reference is kept, and an unpublished table is prepared with the observed inline count `n`, weak = 1 and object = header. For `arc`, it is published by CAS of the **whole control word**, and only success transfers count authority to the table. On failure, the operation retries with the latest count, or frees the unpublished candidate and reuses another published table. Then it retains a Weak and returns it. Exactly one table is ever published; migration changes no strong count and is never reversed. Cyclic objects have a table from the start. Inline increments and decrements also CAS the whole control, rechecking the representation rather than overwriting a newly published table pointer with an old count. `rc` preserves the same transitions without atomics.

`upgrade` returns `None` for strong zero, Aborts at the maximum, and otherwise attempts a checked increment, retrying on an `arc` CAS race. **`table.object` is read only after strong retention succeeds.** If final release wins, the upgrade fails; if the upgrade wins, its strong keeps the object alive. `clone` requires an existing strong, and `upgrade` never resurrects zero. An expired table keeps an immutable stale object pointer, which Weak cleanup must never dereference.

The final strong release changes one to zero, keeps the required descriptor and table information locally, destroys the complete dynamic payload using `destroyValues` when needed, frees the original allocation through `freeStorage`, and then releases the weak guard; the header is never reloaded after the free. Payload Weak cleanup cannot free the table prematurely, because the guard survives the object free. The final weak release frees only the table. Without a table, final strong release needs only payload destruction and the object free, and `obj` similarly destroys and frees without a count decrement. Abort or nontermination prevents the remaining cleanup and free, and adjusted base addresses are never freed. A release without corresponding responsibility is an invariant violation.

#### 21.2.3.3. Runtime ordering

The following completion and effect ordering is normative for `arc`. An arrow requires program order or synchronization, including transitive effects; elapsed wall-clock order is insufficient:

```text
initialize side table -> publish control/Weak -> use table
initialize metadata/payload and finish builder cleanup
    -> publish completed object -> payload use through a runtime-acquired strong
all earlier strong releases -> final strong release -> complete payload destruction
free object -> release weak guard
all earlier weak releases -> final weak release -> free side table
```

The strong-release guarantee must survive inline-to-table migration. These obligations cover runtime initialization, counting, cleanup calls and frees; they define no source-level concurrent payload access, thread transfer or language memory model (Appendix D.2).

**Non-normative implementation candidate:**

| Operation | Candidate ordering |
| --- | --- |
| Resolve the published table from the control | Acquire |
| Publish migration CAS | AcqRel on success, carrying earlier releases |
| Strong/weak retain CAS | Relaxed |
| Successful upgrade CAS | Acquire; failure Relaxed |
| Strong/weak decrement | Release, with an Acquire fence before final destruction or free |
| Unpublished initialization | Ordinary stores |
| Publish cyclic strong 0 -> 1 | Release |

On CAS failure, the latest representation is rechecked; a failure that discovers a table pointer still needs an Acquire observation before accessing the table. Inline decrements use a control CAS, and table decrements may use `fetch_sub`. LLVM spells Relaxed as `monotonic`, and compare-exchange orderings must satisfy its verifier constraints, including no release or acq_rel failure ordering. Alternative implementations must prove the normative arrows. Weak-memory behavior, not just possible interleavings, is validated, and generated IR and native count protocols are verified separately. Allocation and destructors have no lock-free guarantee.

### 21.2.4. Value-borrow storage

`ref/V` and `uniq/V` point to the storage of the **immediate complete `V`**, without implicit dereference. On windows-x64-v1 each is one nonnull address-space-0 pointer with size, alignment and stride 8/8/8, aligned for `V`. It contains no metadata, length, count, Origin or Loan ID. A borrow of an Array or Slice points to its complete handle. `ref/(rc/T)` points to a handle slot, while `objref/T` points to the original object header; `uniq/(rc/T)` grants exclusive access to the handle slot, not unique payload ownership. Nested dependencies stay distinct, and an ordinary `@ref` on an existing shared borrow copies it rather than adding a layer.

A zero-sized `V` keeps its logical initialization, Loans and destruction. It uses nonnull aligned substitute storage that stays alive for the required uses; one static substitute per alignment may serve several distinct Places. Pointer equality merges neither those Places nor their logical overlap. Size and stride remain zero, lifetimes are not extended, and substitute bytes justify no positive `dereferenceable` attribute; `noalias` follows the call contract of §21.5.5, not substitute addresses.

An unknown-layout generic `V` still uses one borrow pointer. A separate GenericContext is passed only if operations on `V` need it; transferring the borrow alone needs no `V` metadata. Direct value layouts must be finite. If materializing a required temporary needs unsupported storage, a verified adapter or specialization is used, or the use is diagnosed; it is never silently boxed or made unsized.

A proven Sealed payload projection uses the ordinary `ref`/`uniq` ABI and points to the payload address of §21.2.3, not to the object header. Its completeness proof is static: it adds no mode, count operation, header field, generation counter or runtime Loan representation, and the ownership dependencies stay in the existing analysis facts.

### 21.2.5. Concrete Closures and common function values

#### 21.2.5.1. Concrete environment

A concrete Closure environment `E` is a direct aggregate of its complete captured Types. Binding Identity, logical capture order, mutability, Move Paths and Origin/Loan information are kept separate from physical offsets, and the alignment-sorted aggregate layout of §21.1.3 applies. `E` has no embedded call pointer or header, user `deinit`, public fields or reflection members. Parameters, results and aggregate storage do not force it onto the heap. `E` is Copy exactly when all captured complete Types are Copy. An empty `E` and Function Items have size, alignment and stride 0/1/0; zero-sized captures keep their maximum alignment. Function Item identity, bound arguments and the selected implementation are static information.

Calls use the Shared `ref/E`, Exclusive `uniq/E` or Consuming `owner/E` receiver of §7.6.3. An owning Callable contract acquires `E` normally before adapting to a weaker implementation requirement. Shared and Exclusive calls neither own nor destroy `E` and cannot Move owned captures. Consuming calls clean up only the remaining initialized captures, in reverse logical acquisition order: the implicit owned `E` binding precedes the explicit parameters, so the result is secured, then body locals and `defer` are cleaned, then the parameters, and finally the remaining `E`. Partial environments use CFG state, with flags only where joins require them, not a mandatory per-Closure bitmap; an incomplete `E` can never be acquired, erased or called. Zero-sized captures still run every required destructor.

#### 21.2.5.2. Common function storage and entries

A common Function Type `F` remains Owned-environment, Shared-callable and Non-Copy (§7.6.4). Its Windows handle has size, alignment and stride **16/8/16**:

| Offset | Field | Meaning |
| --- | --- | --- |
| 0 | `environmentWord`, 8 bytes | `E` inline when `size(E) <= 8` and `alignment(E) <= 8`; otherwise a nonnull pointer to `E` |
| 8 | `operations: ptr` | Nonnull immutable FunctionOperations pointer, aligned to 8 |

The environment's layout selects the mode; there is no tag. Unused inline bytes are unspecified, and the word need not be an integer or a pointer. Even a zero-sized `E` with alignment 16 uses heap mode. A heap `E` is allocated with `Alloc(size(E))`, using the allocator's zero-size backing and 16-byte alignment, without a header, prefix or alignment argument; alignment above 16 is unsupported.

**FunctionOperations** is 24 bytes, aligned to 8: +0 a nonnull `callEntry`, +8 a nullable immutable `context`, +16 `destroyEnvironment`. It is keyed by `E`, `F`'s contract, the selected implementation, the context schema and contents, and the FunctionAbi, not by `E` alone. Its first two fields have the callee-record shape of §21.3.3, which alone proves no ABI compatibility. `destroyEnvironment` is null exactly when neither environment cleanup nor a heap free is needed; a heap entry is nonnull even for a cleanup-free `E`.

The logical entry arguments are as follows; their physical positions remain FunctionAbi choices:

```text
callEntry(environmentWord, arguments..., context) -> result
destroyEnvironment(environmentWord, context, location) -> ()
```

Both entries receive the **environmentWord value**, not the address of the `F` slot, through the matching FunctionAbi. Heap mode addresses `E` directly; inline mode may spill the valid `E` bytes when an address is needed, preserving pointer provenance rather than prescribing an integer register. This ABI transfer is neither a language Copy nor a capture reacquisition. An inline Shared-call spill is a temporary view without destruction responsibility. The receiver Loan protects the original `F` and its environment through the whole call. No borrow into the hidden `E` may escape, and no environment address is promised across calls; pointers to external captures keep their original targets. Calls need no per-invocation allocation.

Destroying `F` consumes its responsibility and invokes its destruction entry at most once: it destroys an inline `E` only, or destroys a heap `E` and then frees it. A null-entry destruction still consumes `F` logically. Moving `F` transfers its 16 bytes, with no self-pointer into the inline word, capture reacquisition, allocation, count increment or required source clearing.

#### 21.2.5.3. Erasure, direct calls and optimization

Only a complete, Owned, Shared-compatible environment with a compatible signature may be erased. Results may borrow public arguments but not the hidden environment. Borrowed environments, Exclusive or Consuming common Types, common-value cloning and FFI callbacks are not introduced.

For a conversion directly from Closure syntax, the uninitialized final `F` and environment storage are prepared before the captures are acquired, and the captures are then acquired directly there in source order. Capture entries are binding Copy/Move/Borrow/Reborrow operations, not arbitrary user expressions, and their static effects and failure order are preserved. A required allocation failure reports the conversion site. For a general source expression, the expression is evaluated once **before** any conversion allocation. An `F`-to-same-`F` acquisition is an ordinary Move, not a re-erasure. A completed `E` acquisition and the final transfer are combined only when the alias, lifetime, ordering and partial-state conditions of §21.4.4 hold; a live `F` is never overwritten early.

In the `makeAdder` example of §7.6.4, the `i32` capture occupies four inline bytes and the common function handle 16 bytes. Repeated Shared calls keep that environment, and moving the handle transfers its ownership.

Known Function Items and Closures call their selected entries directly, and `F` uses `callEntry`. A shared generic Callable call uses the compile-time witness plan's selected entry/context pair, without constructing `F` or a runtime witness table. Definitions, calls and adapters share one FunctionAbi mapping for the logical receiver, arguments, result and context; matching `ptr` signatures alone are insufficient. Adapters never reevaluate source arguments, add a language Copy or reselect overloads or specializations. Shared destruction, free and adapter cleanup keep the original operation's source location, while failures in user `deinit` bodies keep their own locations.

Allocation count and location are not language guarantees. Elision need not reproduce the failure of a removed allocation or free, but must preserve acceptance, required checks, captures, user effects and destruction order; surviving resource operations keep their Abort and source-location contracts. Promoting a heap environment to the stack requires tracing all uses and cleanup and replacing the entries with matching direct code. Stack storage is never passed to a heap-freeing entry, and no operation-table variants are added just for promotion; otherwise the heap representation is kept. Physical ABI choices remain compiler-controlled under §21.4.2.

## 21.3. Generic code generation

The rules here and in §21.4.6 define generic sharing and specialization; [Appendix B.7](appendices/B-reference-models.md#b7-generic-generation-and-specialization-strategy) records non-normative compiler guidance. Internal names describe plans, not new syntax, reflection, JIT or a stable external ABI.

### 21.3.1. Policy and sharing conditions

**Instantiation** binds generic arguments. **Explicit full specialization** selects a user-written implementation under §8.8. **Automatic specialization** fixes facts in generated code while preserving the selected implementation. Neither instantiation nor a Scalar operation alone requires a separate machine-code body.

Generation has these logical dependencies, without prescribing compiler passes:

1. Verify the generic body universally under its declared premises (§8.10), including operations, ownership, Loans and cleanup.
2. Check each use and select its implementation from the closed explicit-specialization set.
3. Substitute the verified plan and resolve concrete layout, acquisition, effects and cleanup.
4. Build a correct baseline sharing plan, then apply bounded optional optimization.

Sharing and budget changes preserve semantic acceptance, selected implementations, results, evaluation and effect order, checks, ownership and cleanup. Runtime metadata executes verified operations; it never performs lookup, proves Constraints or validates source Loans. Origins stay in semantic proofs and dependencies even when generation keys erase them. Pattern coverage and required unreachable-code diagnostics are unchanged.

| Generation choice | Condition |
| --- | --- |
| Baseline sharing | Common representations, typed helpers, operations, storage and ABI adaptation preserve the verified plan |
| Required separation | Those mechanisms cannot preserve a semantic or representation difference |
| Optional specialization | Fixing facts improves the baseline without changing its meaning |

Scalar arithmetic and typed loads and stores may use small typed helpers while a larger body remains shared; helpers keep the original checks, failure order and source location. No policy grants writes through `ref`, implicit Copy through `uniq`, or pointer comparison in place of a required referent operation. Unsupported paths are diagnosed before emission rather than generating a semantic substitute.

~~~kimi
func classify<T>(value?: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func forward<T>(value?: ref/T) -> i32 => classify<T>(value)
// forward<i32> calls the explicit implementation even with specialization budget 0.
~~~

### 21.3.2. Identity and generation keys

Semantic identity, implementation selection and generated representation are kept distinct:

| Identity or plan | Required information |
| --- | --- |
| Complete static Type identity | Normalized nominal identity, every Semantics layer, nested Types and Origins; used together with Loan information |
| Implementation Selection Key | The original function identity and the ordered ArgKey/LengthKey values of its own generic slots; selects within the closed definition-side set |
| Runtime Object Type Identity | The CoreId of the actual payload, using the root-handle rule of §21.2.1 |
| Generation plan | The selected implementation, typed operations and cleanup, entry and body ABI, reader schema, and all embedded facts needed to preserve meaning |

Complete arguments are normalized and validated before an Origin-erased key is formed. Selection keeps the outer handle Semantics; it is not object identity. Origin differences alone neither select another implementation nor require machine-code duplication. Equal size or pointer representation proves no semantic equivalence.

A body may omit concrete Types that are handled wholly by context or helpers. Embedded signed arithmetic must still distinguish `i32` from `u32`, and direct destruction must keep the exact entry and embedded context. When destruction remains indirect, the actual operation/context pair is supplied. Equal size and a has-destructor flag cannot justify erasing different destructors. Type identity, offsets, witnesses and other used facts each need a valid sharing plan.

[Appendix B.7.1](appendices/B-reference-models.md#b71-plan-keys-and-generation-order) separates semantic, body, entry and context keys for the initial compiler. These are in-memory generation roles, not a persistent object-cache format; persistence follows §21.3.4.

### 21.3.3. Shared operations and Type policies

#### 21.3.3.1. Requirements and fixed facts

Shared, fully fixed and partly fixed bodies use the same lowering. Each typed operation obtains only its required facts from the verified substitution plan:

| Binding | Lowering |
| --- | --- |
| `Fixed(value)` | A constant, direct instruction or known entry |
| `FromContext(slot)` | A read of the schema-defined slot Type |
| No requirement | No slot |

Requirements need not correspond one-to-one with Type parameters: a body may independently need a size, a Field offset, a length or a selected callee. A borrow transfer alone needs no referent metadata; a value transfer needs its size and valid storage; destruction, Contract operations and calls use selected entry/context pairs. Typed helpers supply Scalar operations and ABI adaptation.

Sizes, alignments, offsets, identity, acquisition conditions and operation pairs are derived from the **same substitution**. Every premise of embedded instructions and attributes is recorded as a fixed fact and proven for every connected substitution; matching slot storage Types or sizes are insufficient.

~~~text
Fixed facts:
    size(T) = 16
    source and destination are valid, nonoverlapping 16-byte ranges
    both alignments are at least 8

Allowed: memcpy 16 bytes with align 8
Unproven: align 16, a derived Field offset, or a particular destructor
~~~

LLVM `align` and `dereferenceable` use proven constant bounds; attributes are weakened or omitted when the common guarantee is insufficient, and a runtime alignment slot is not a constant attribute. Stronger facts may apply within a proven branch, helper or specialization. `noalias` is proven under §21.5.5, and `inbounds` under its own pointer contract.

Width, signedness, floating-point rules, Semantics, effects and partial state are kept in the typed plan. `obj`/`rc`/`arc`/`objref`/`objuniq` acquisition and count operations stay distinct until equivalent low-level operations can safely merge. Unit keeps its zero-sized state and effects, and Never has no value or normal return. Raw-pointer pointee layout and Unsafe preconditions are preserved.

Metadata only runs acquisition plans already proven valid under §8.10. A conditional Copy/Move plan may use HasCopy, but runtime flags never establish source legality. SharedReadResult (§4.6.6) remains a full-Semantics plan: owner Copy elements yield values, owner Non-Copy elements yield `ref`, object owners yield `objref` without retaining counts, and exclusive borrows yield shared Reborrows. Correlated result Types, Origins and ABI mappings are preserved; slot borrowing is a different operation.

The Copy/Move and duplication examples of §8.10 also apply to shared lowering. In contrast, a pure borrow transfer can omit referent metadata entirely:

~~~kimi
func keepBorrow<T>(value?: ref/T) -> ref/T from value => value
// No referent access, so no T metadata is required.
~~~

Whole-value updates use a common checked target/acquisition/transfer plan that carries complete Type, Loan, Origin and destruction-responsibility evidence. Sealed proves completeness but does not identify a concrete entry in shared generic code; devirtualization happens only when a concrete generation entry is known. Validated summary and cache dependencies are reused, and plans are invalidated when Type formation, openness, effects or specialization change.

#### 21.3.3.2. GenericContext and operation pairs

A GenericContext contains only the facts its reader needs. In windows-x64-v1 it is an immutable, schema-defined sequence of 8-byte slots, slot `index` at offset `8 * index`:

| Logical slot kind | Contents |
| --- | --- |
| 64-bit integer | Nonnegative `isize` lengths, sizes, alignments and offsets; full-width `u64` Type tokens; Booleans 0 or 1 |
| Entry pointer | A selected operation entry |
| Context pointer | That entry's private context, required metadata, or null |

The typed schema fixes each integer's signedness and purpose; its LLVM storage is `i64`. Layout and length values and slot arithmetic are checked against profile limits at generation time. A `u64` Type token is never narrowed to a nonnegative `isize`, pointers are never converted to integers, and no runtime slot tags are added.

An operation uses adjacent entry/context slots when both components are dynamic; fixed components and unused requirements may be omitted. Contract witnesses and FrameLayout are compile-time correspondence plans, not runtime tables reached from a GenericContext. Related Types, requirements and selected implementations stay in the semantic plan.

Ordinary shared bodies read sizes, offsets and lengths directly, without traversing TypeContext. Facts used only by a callee stay in that callee's context. Identical semantic requirements are merged, but distinct requirements are not merged merely because their current values happen to match.

~~~text
Example context for a body using comparison, instantiated with T = i32:
    [0] isize   size(T)          = 4
    [1] entry   selected compare = compare_i32
    [2] context compare context = null
    [3] isize   offset(temp1)    = 4

If temp1's offset is fixed for every member of the body, omit slot 3.
Invoke slots 1 and 2 using the comparison entry's ABI.
~~~

A direct integer needs one slot read, and a fully dynamic operation pair at most two. This describes the access path, not final instruction counts or a speed guarantee: the operation may read its own metadata, and flattened contexts may use more bytes.

#### 21.3.3.3. Type metadata, schemas and cycles

ValueMetadata keeps the 48-byte format of §21.2.2. TypeContext serves the **Type operation entry** and may hold Field offsets, element and Case metadata, and selected operation pairs; a fixed-array TypeContext includes `elementMetadata` and `N`. The entire derived TypeLayout is computed from the closed substitution, and body integer slots are derived from the same plan instead of rebuilding layout at runtime. Handle metadata cannot substitute for object payload metadata.

A schema describes reader requirements, definition-side bindings, normalized Type and length expressions, operations and slot Types. It is deduplicated and ordered by stable structural requirement keys, keeping each dynamic operation pair adjacent; source occurrence, registration order and parallel completion do not determine the schema. A callee's internal schema is not part of the caller's schema.

Each entry is generated and validated together with its own context. The caller passes that context opaquely and never interprets its schema. Reader changes may remove unused slots without schema-only adapters, while ABI differences still require the adaptation of §21.3.6.

Metadata and contexts are immutable constants for closed substitutions and may form finite cycles, including mutual recursion; a DAG is not required. After optional choices are made, final references are resolved, and all pairs and fixed facts are validated before executable output; incomplete records are never published. A changed callee context need not change the caller ABI or optimization choice.

Records and entries stay alive through every use and cleanup, independently of caller stack storage. No per-call context construction or runtime Type search is required. Shared metadata stores no instance initialization state, Loans or dynamic collection lengths, and a null context denotes no required context.

#### 21.3.3.4. Lengths and fixed-array destruction

Length meaning and formation proofs remain owned by §4.4:

| Use | Source |
| --- | --- |
| Explicit-specialization selection | Static LengthKey |
| Body `N`, bounds, Slice length and element loops | Integer slot or Fixed |
| Array transfer size | Integer slot or Fixed, from the same TypeLayout |
| Fixed-array destruction | `elementMetadata` and `N` in the array's TypeContext |
| A length needed only by a callee | That callee's private context |

Length requirements are identified by normalized expressions and definition-side bindings. Type formation is proven from declared premises and concrete evaluation is checked; body-only Type formation cannot wait for favorable arguments. `N` is never inferred from size and stride, and a body never reads `N` through TypeContext. Keeping `N` both in body slots and in destruction metadata is permitted when both readers need it.

~~~kimi
func keepArray<length N, T>(value?: [N of T]) -> [N of T] => value

func lengthAfterOffset<length N>(value?: ref/[(N + 4) of u8]) -> isize
    return N + 4

func twiceLength<length N>() -> isize => N * 2
// keepArray transfer needs size only.
// The signature's ValidLength proof can eliminate the N + 4 calculation.
// N * 2 is ordinary checked arithmetic: overflow remains runtime Abort.
~~~

Authorized transfers follow §21.4.5, and fixing a length alone does not remove unrelated arithmetic checks. For example, specializing this loop for a small `N` may remove proven bounds checks or unroll the loop, but must keep any unproven total-overflow check:

~~~kimi
func sum<length N>(values?: ref/[N of i32]) -> i64
    var total: i64 = 0
    for i in values.indices
        total += values[i]@i64
    return total
~~~

For byte-offset multiplication attributes such as `nuw`, it must be proven that `0 <= i < N` for the same array dominates the multiplication, that the stride is nonnegative, that `N * stride` fits the operation width and the layout limit, and that the operands are defined. Pointer provenance, addition and `inbounds` need separate proofs. A zero stride does not remove logical bounds checks.

Destruction uses the pair `{destroyValues, metadata}`; `metadata` is the existing entry's context argument in `destroyValues(first, count, metadata, location)`, not an extra argument, and its operation ABI need not order arguments like an ordinary call. The null-entry, zero-count, complete-range, reverse-order and source-location rules of §21.2.2 apply.

A fixed-array entry reads `elementMetadata` and `N` from its own metadata's TypeContext. It destroys the outer `count` arrays in reverse order and each array's `N` elements in reverse order, skipping the calls for null element entries or `N = 0`; storage release is separate.

~~~text
arrayDestroy(first, count, arrayMetadata, location):
    elementMetadata, N = arrayMetadata.TypeContext
    for each array in reverse logical order:
        if N != 0 and elementMetadata.destroyValues != null:
            elementMetadata.destroyValues(arrayFirst, N, elementMetadata, location)
~~~

A complete contiguous range may become one `count * N` element-destruction call only when the metadata, range, reverse order and failure location agree. If that product exceeds the count width, the original loops are kept, without adding an Abort. All logical destruction effects are preserved, even at zero stride.

Inherited group static storage is another supplied operation: it ensures initialization and reaches the Field's normalized storage key (§22.2.4). It uses a fixed operation or an existing entry/context pair, preserving one mutable state per key and an immutable operation context. Touching such a Field alone does not require separate machine-code bodies.

### 21.3.4. Artifacts, verification, and invalidation

#### 21.3.4.1. Closed selection and dependency validation

The defining Kotonoha closes its explicit-specialization set under §8.8.3. Artifacts keep declarations, selection sets and mappings, complete contracts, verified bodies and legitimate deferred obligations, and preserve defining Symbols, environments and dependencies without exposing private names to callers.

Every environment-selected specialization's target, arguments, Constraints, inherited contract and body are checked before its artifact is finalized, even if unused; excluded syntax follows §19.5. Unused verified bodies need no machine code, while every use still checks its own contract, initialization and Loans. Shared calls and function references reach the statically selected implementation, never one selected from a value's Dynamic Type or from registration or load order.

The complete declaration, body, selection, premise and absence dependencies are preserved and validated under [§18.7](18-modules-and-dependencies.md#187-verified-information-and-reuse). Changed selections are revalidated before generation; old proofs are never mixed with new mappings, and no incorrect shared fallback is chosen.

Nested declarations share one declaration tree, with interned normalized references and reusable parent bindings; children are not cloned per instantiation, and resolving a child does not require the outer layout. Artifacts keep bindings, Constraint roles and origins, proof dependencies and selected declarations, and outer changes invalidate dependent inner proofs and plans. Adding or widening an accessible Container name on an open base can break derived declarations and requires downstream revalidation (§9.6.1). Resource-limit diagnostics are distinct from failures of the specified proof rules.

#### 21.3.4.2. Persistence and composition

Persistent semantic plans, invalidation and observable source information follow §18.7. ABI plans, schemas, contexts, frames, budget choices, IR and machine code are regenerated; native input summaries are separate. Persistent product generation choices remain deferred until measured search cost and a complete validation contract justify them. Such a contract must cover baseline plans, candidate sets and order, estimates, budgets, compiler and profile, selected implementations and generation dependencies; a small saved choice is not proof of validity. Object-code caches and persistent runtime context graphs are not introduced.

The operations, selected implementations, schemas and constants actually used by generated artifacts are recorded. Changes to these dependencies revalidate or regenerate the affected witnesses, inlined bodies and entry/context pairs; conservative invalidation is allowed until precise dependencies exist. Source distribution and deferred binary interfaces follow §18.3–§18.7. These rules define no CompositionId or Entry/Provider selection; future Composition Root connections require a separately adopted contract (§13.8).

### 21.3.5. Generation limits and code merging

**Mandatory generation resource limits** are separate from **optional optimization growth budgets**. Identical inputs, compiler and profile, and settings reproduce the logical plans and budget allocation independently of enumeration, parallel completion and cache presence. This does not promise identical behavior under actual OS resource exhaustion.

Required closed substitutions, layouts, metadata, contexts and plans must fit finite generation limits. Finite graph cycles, growth of distinct keys such as `T -> Box<T>`, and invalid infinite inline layout are distinguished. Body sharing alone does not bound metadata generation. Existing logical plans are reused for already registered keys; the full Cartesian product of arguments is never enumerated, and fake pointers are never used for missing facts.

Failed or exhausted optional exploration keeps the verified baseline and cannot by itself reject source. If required baseline generation also exceeds the limits, a resource diagnostic is issued, separately from semantic or representation errors. Required checks and unsupported-feature diagnostics are kept even for definitions with no emitted machine code. Budgets never replace an explicit specialization with the ordinary body.

Equivalent code and entries may merge only while preserving identity, results, effects, failures, ownership and cleanup. Needed immutable metadata, operation and context records are emitted as private `unnamed_addr` constants when their addresses are unobservable. Constant folding and direct calls keep the typed plan and the consumer contract. TypeLayout, ValueLowering, FunctionAbi and CleanupPlan are reused rather than cloning syntax.

[Appendix B.7](appendices/B-reference-models.md#b7-generic-generation-and-specialization-strategy) gives the initial bounded selection and accounting method. Budget numbers and estimates are neither fixed language constants nor exact limits on final binary size or compilation time. Local specialization hints, dynamic storage for unknown substitutions and object caches remain deferred under Appendix D; no syntax to require or forbid specialization is added.

### 21.3.6. Entry ABI and call responsibility

#### 21.3.6.1. Budget-independent entries

The caller-facing entry ABI is chosen from the call contract:

| Call | Entry ABI |
| --- | --- |
| Fixed callee with known substituted signature representations | The same FunctionAbi rules as a nongeneric function |
| Context callee pair, or unresolved signature representation | The shared ABI, based on the generic declaration's signature |
| Common function value | The existing Function Type contract (§21.2.5) |

The shared ABI passes representation-dependent acquired values by storage pointer, and results by pointer to caller-provided uninitialized storage. A declaration-known common representation is used directly, and `ref`/`uniq` keep their existing one-pointer form. A Fixed entry alone does not make an unresolved signature scalar.

Optional budgets change entry implementations and internal adaptation, **not the entry ABI used by callers**. The needed entries are generated per call contract and connected to shared or specialized bodies, and entries merge only when their ABI and complete implementation facts agree.

~~~text
concrete caller -> direct_i32_entry(i32, i32) -> i32
                       -> adapt to storage -> shared body
                       or -> specialized body

shared caller -> {shared_entry, private context}
                       -> shared body or adapted specialized body
~~~

When the ordinary FunctionAbi uses scalar passing, the concrete caller keeps it even at budget zero; extra transfers and calls inside the entry may remain. Reverting an optional candidate reconnects its entries to baseline bodies without specializing or rolling back a recursive caller group. Generic scratch frames follow §21.4.6.

#### 21.3.6.2. Connection validation and adapters

The logical call unit is `{entry, context}`; pair generation and opaque context handling follow §21.3.3.3. At every connection, the **generation scheme identity** and the entry ABI are matched statically. The scheme covers the compiler build, target and profile, ABI/metadata/context generation rules, and settings that change those rules.

The entry ABI records the logical arguments, results, ownership and context, together with their physical Types, positions, calling convention and attributes. Indirect calls need this typed contract, not runtime ABI tags per slot. Callee schema changes are internal to the entry/context pair, and no adapter exists solely to translate schemas.

Within one scheme, real ABI differences such as a scalar result versus result storage are adapted. Artifacts from another scheme are regenerated or rejected rather than adding scheme adapters or runtime checks. OS startup, FFI and external backend helpers keep their external contracts. Later argument elimination or calling-convention changes require a proof of compatibility across all uses; symbols, emission of unused entries and physical argument positions are not public guarantees.

#### 21.3.6.3. Acquisition and cleanup

The responsibility transitions of §21.4.3 apply at the **logical callee entry**, regardless of how entries, adapters and bodies are split. The receiver and arguments are evaluated and acquired once, in language order, and adaptation adds no language Copy or Move and no duplicate cleanup. Owned argument pointers, `ref`, `uniq` and result storage keep distinct contracts despite identical pointer representation. The same normal-return and caller-storage rules apply to generic scratch reserved by an entry (§21.4.6).

### 21.3.7. Product and test generation

For code generation, product substitutions, sharing classes, call entries, frames and budget choices are fixed first, from product inputs and normal-output roots. Test processing does not require a valid product entry: when ordinary startup selection has no unique valid entry, its product startup root set is empty; Library/public generation roots retain their ordinary meaning. When an ordinary entry is valid, plan its product closure without executing it. In either case verify all selected product bodies and never choose roots from a test filter. Semantic-only test checking and `--list` need not construct this plan. Source membership and dependency partitions follow §18.8.

Additional test requests, including new substitutions of product generics, belong to the test region. Existing product entries are reused for identical substitutions. A new test entry may connect to an existing product body only after the existing contract is validated; product sharing classes, context schemas and frames are never enlarged, and required additions go into the test region.

Product and tests are accounted separately, with all test cases sharing one test budget. Candidates, savings and unused budgets are never transferred between regions. Reused product code does not count again in the test budgets. External source bodies count in the region that requests their generation, while separately linked existing native code does not. Ties are broken using product declarations and semantic information, not SourceIds or whole-input hashes that include test source.

```text
fixed product plan and budget
  -> tests reuse existing product entries
  -> test-only substitutions, bodies, and budget
  -> emit the required test generation closure
```

After the plans are fixed, only the closure needed from all test execution roots is emitted, including selected implementations, initialization and destruction, cleanup, runtime helpers, entries and contexts, and metadata. Omitting product code neither redistributes product budgets nor omits required verification. Case filters do not change the compiled all-case artifact (§20.9, §22.6).

Test artifact inputs and settings are fixed under §18.8. Product plans are reused only under the validity conditions above, and changed dependencies or settings require the affected product plans to be revalidated and fixed before test requests are added. This requires neither two full compilations nor two output files: one final LLVM module may hold both regions. Different execution roots and reachability, and later optimization, need not produce byte-identical whole binaries. Entry/Provider composition remains deferred and is not an implicit test-build input.

## 21.4. Checked lowering and internal ABI

### 21.4.1. Input and generation set

Lowering consumes finalized, read-only semantic information: complete Types, selected implementations and callees, acquisition and evaluation order, CFG edges, initialization, consumption and destruction responsibility, validated Loans, Origins and lifetimes, cleanup plans, source locations, and supported layout, ABI and runtime operations. It neither re-resolves names nor reconsiders ownership legality. A separate target-independent lowered IR is optional; direct LLVM generation from the verified CFG and semantic information is allowed.

Stable identities are shared without cloning the syntax tree. These distinct records are computed and reused:

| Record | Information |
| --- | --- |
| TypeLayout | Mode, size/alignment/stride, LLVM storage Type, mapping from Field/base identity to offset |
| ValueLowering | Computation and storage representations, conversions, valid-bit constraints |
| FunctionAbi | Physical signature, argument and result slots, calling convention, ABI and justified optimization attributes |
| CleanupPlan | Edge-specific initialized and moved parts, ownership, registration, logical destruction order |

Unresolved Types or obligations, missing cleanup and unsupported selected operations fail generation. `zeroinitializer`, `undef`, `poison`, `unreachable` and `freeze` are not substitutes for unresolved language semantics.

Selected product implementations, startup, required concrete generic implementations, dependencies, Kimi, cleanup and runtime helpers are planned from the normal-output roots; foreign imports have no emitted body. A worklist keyed by declaration identity, concrete arguments and selected implementation is used: ordinary recursion reuses a declaration, and unbounded distinct instantiations receive a resource diagnostic. After required verification and planning, unneeded machine code and metadata are omitted; test emission follows §21.3.7 without reallocating product budgets.

Unused nongeneric bodies, and unexecuted branches within generated bodies, still receive the required semantic and unsupported-feature diagnostics; code omission cannot hide them. Excluded syntax is outside this set, and verification of uninstantiated generic bodies is unchanged. Unsupported sharing or metadata must not silently select another implementation. Code may be omitted or removed only after the required checks.

### 21.4.2. Physical function signatures

The internal function ABI is compiler-controlled within a final generation and deliberately unfixed. Source dependencies may participate in that common generation, and no external ABI is needed between their separately verified semantic plans. The ABI applies to user functions, Kimi and private runtime helpers, in Application and Library inspection output. No physical calling convention, parameter or result representation or ordering, hidden-context position, symbol spelling or stable ABI version is a language guarantee. The compiler may choose different physical signatures across builds, targets and generated functions without a language-version change, subject to language and external contracts. Within a scheme, generic callers use the entry contracts of §21.3.6: optional budgets change implementations, not the caller-facing entry ABI. Independently generated modules or compiler builds have no promised binary compatibility.

Within the entry rules of §21.3.6, direct or indirect passing, aggregate splitting and coercion, result storage, omitted slots, calling conventions such as LLVM `ccc` or `fastcc`, and ABI attributes such as `byval` or `sret` are compiler choices. Storage layouts defined elsewhere, including those of Scalars, `string`, object handles and `Kimi.Weak<S>`, do not fix function passing. Unit still has its logical value and effects, and Never still has no normal result or return edge. Every emitted representation requires an implemented ValueLowering and its complete validity, ownership and cleanup operations; implementation freedom never permits guessing an unsupported representation.

Definitions and all calls, including indirect entries and adapters where supported, derive from the same FunctionAbi contract, and physical rearrangement must preserve the mapping to logical parameters and results. Explicit arguments are evaluated and acquired once, in source order. A value receiver is evaluated once before the explicit arguments (§7.3); a Type-qualified unbound call supplies `self` in ordinary argument order. Physical slot order does not determine evaluation order. ABI attributes are selected according to the actual backend contract, and any additional validity or optimization premises are proven; a calling convention or attribute cannot grant source-level Copy, Move or alias permissions.

An ABI change must update every affected definition, caller, adapter and compiler-generated runtime helper consistently, and invalidate incompatible generated and cached artifacts. The generation scheme identity and typed entry contracts of §21.3.6 are used; no separately published internal ABI version or compatibility window is required. This initial design rebuilds generation artifacts rather than persisting them (§21.3.4). The specified OS entry, foreign C calls, backend-support symbols and externally observable storage keep their own contracts. In particular, `backendSupport.abiVersion` identifies the external backend supply, not a stable Kimigayo function ABI.

**Non-normative example.** One compiler implementation can use direct scalar passing and caller-provided aggregate slots as follows. These signatures illustrate a possible lowering, not a required or frozen ABI.

```kimi
group Samples
    func isPositive(value?: i32) -> bool => value > 0
    func echo(text?: string) -> string => text
    func echoPair(value?: (string, i32)) -> (string, i32) => value
```

```llvm
; Fragments omit profile attributes. echo needs an internal definition in final IR.
define internal i1 @kimi_is_positive(i32 %value) {
entry:
  %result = icmp sgt i32 %value, 0
  ret i1 %result
}
; Physical echo signature: void(ptr %result_storage, ptr %text_storage).
; echoPair can also use two pointers: an independent result slot and an acquired tuple slot.
```

### 21.4.3. Slot responsibility and normal return

The following responsibilities are language semantics, independent of the physical ABI. Argument temporaries and secured results are logical values; the compiler need not allocate a separate physical slot for each. The table also describes a caller-provided result slot when one is used.

Arguments are acquired once, in source order. A Copy preserves its source, and a Move transfers responsibility into the argument temporary; allocating a slot is not a Copy. A Copy source cannot share mutable storage with the callee's acquired value when that would expose consumption or modification of the source.

| Point | Acquired argument values | Secured result / optional result storage |
| --- | --- | --- |
| Acquisition, just before the call | Caller responsibility | No result yet; optional storage is uninitialized |
| Callee entry | Responsibility transfers to the callee | No result yet; optional storage is uninitialized |
| Result secured, cleanup running | Remaining parts are callee responsibility | Secured, still callee responsibility |
| Normal return edge | Consumed or cleaned; the caller must not destroy them again | Responsibility transfers to the caller, whose Initialized fact begins here |
| Abort or nontermination | No later normal cleanup or return | The caller neither reads nor destroys it |

A transfer during argument acquisition uses the caller's cleanup plan for the already acquired temporaries; a callee that was never entered cannot clean them up. The callee never frees caller-owned stack storage. Securing a result before cleanup does not make it available to the caller: cleanup must finish before return, and if it Aborts or diverges, later cleanup and result delivery do not occur.

Omitting physical storage for a zero-sized argument or result preserves evaluation, acquisition, parameter responsibility and initialization on normal return. If a zero-sized value needs an address, the implementation must satisfy its alignment and lifetime without changing its language size or stride; one possible implementation is a one-byte substitute slot such as `alloca i8, align 8`. Shared substitute slots meet the maximum required alignment and stay alive through the last use, while Place identities keep separate state and responsibility. Their existence grants no positive `dereferenceable` guarantee for the semantic zero-byte value.

### 21.4.4. Values, Places, and control flow

Acquisition, Place evaluation, first placement and replacement stay distinct. First placement writes uninitialized storage without destroying an old value. Replacement secures the right-hand side, evaluates the left Place, destroys its remaining old parts and then places the new value. A setter receives the secured value instead of an automatic old-value destruction and store, and simple assignment does not call the final target's getter. Compound assignment remains target-first (§13.7).

Standard stored access uses TypeLayout; custom, computed and required access keeps the selected callable contract, including its restrictions after witness optimization. Self-assignment cannot be removed based only on address equality.

```kimi
values[index()] = makeValue()
// makeValue -> index -> old-value cleanup -> placement
let ok = divisor != 0 and (100 / divisor > 1)
// Division and its checks run only on the true edge of divisor != 0.
```

**Joins.** Short-circuit operators, branches, match guards and loops use CFG edges, and conditions and subjects are evaluated once. Scalar joins use `phi` from the actual normal predecessor blocks, which are the blocks after cleanup. Aggregate joins use a common, initially uninitialized result slot, secured on every normally arriving path. A path may secure its result directly in that slot before its required cleanup; this does not deliver the result to the enclosing expression until cleanup completes and the path arrives. If cleanup Aborts or diverges, the enclosing expression neither reads nor destroys the secured result. This avoids an additional transfer on the arrival edge while preserving the acquisition, cleanup and delivery order of §16.2. Unit needs no `phi`, and Never supplies no fictional value or edge. `select` may replace conditional evaluation only when evaluating both alternatives early is proven legal.

A non-Never Expression Type does not guarantee a normal CFG predecessor, because required cleanup may prevent delivery (§14.9); no incoming value is manufactured, and the checked Type is not changed to Never in that case.

**Match dispatch.** Match verification reachability is distinct from ordered runtime dispatch. Arms excluded by earlier Patterns still receive the required checking but contribute no runtime predecessor, result arrival or lifetime update. A separate dispatch plan must preserve the verified state at every selected arm. Unguarded Pattern tests have no acquisition or cleanup effects, while guards require their own state transitions. When prior tests and exhaustiveness guarantee that the next arm succeeds, dispatch may enter it directly without inventing an unmatched value or failure path.

Verification carries earlier false-guard effects, after guard cleanup, into the checking of later arms. A source-ordered verification chain may conservatively keep mismatch edges even for irrefutable or covered Patterns, joining those states with the guard-false states at the next test, but must not add an unmatched normal completion. Runtime pruning removes only proven-impossible paths; it neither resets earlier guard effects nor initializes body bindings before successful guard cleanup.

**Direct construction** into an uninitialized final slot may remove intermediate transfers only if evaluation order, aliasing, Loans, storage identity and lifetime, intermediate observations, partial initialization and cleanup remain unchanged. Otherwise an independent temporary is kept; a live replacement target is never overwritten early.

```kimi
var useOriginal = true
var pair = ("old", 1)
pair = if useOriginal => pair else => ("new", 2)

let saved = work: do
    defer => pair = ("later", 3)
    exit to work: pair
// saved receives the pair secured before defer. pair holds ("later", 3).
```

The result slot may contain secured bytes while cleanup runs, but consumers of the enclosing expression wait for normal arrival. Reusing a slot for another evaluation starts a new logical lifetime; a previous evaluation's arrival does not authorize consumption in the new lifetime.

### 21.4.5. Cleanup and physical transfer

Each scope-leaving edge secures its result, performs exactly the cleanup of the scopes left, and then delivers the result or transfer. The inner-to-outer, reverse-lexical order of locals and `defer` of §16.2 applies, and only registered `defer` bodies and initialized parts still owned are processed. Edge-known state is used directly, and runtime flags are introduced only where paths must remain distinguishable after a join. Equal cleanup sequences may share code when state and destination match. No heap stack or Closure per `defer` is required. Partial construction, Move or base state is never inferred from value bits or addresses, cleanup is never reordered by physical offsets, and destruction is never moved to the last use.

Abort stops cleanup, and nontermination blocks subsequent cleanup and delivery. A lack of visible side effects does not license removing a language-permitted infinite loop.

Tuple and fixed-array construction may place components directly in their final subslots. Each completed placement keeps its own cleanup responsibility until whole-value completion transfers those responsibilities to the aggregate; completion need not copy bytes again. Physical offsets follow TypeLayout, while acquisition and cleanup keep their logical order. This optimization never publishes a partially constructed whole value.

For `replace`, destruction happens at the old location before placement. Rewriting it as an exchange followed by destruction is valid only when location, order, dependencies, observable effects, Abort and divergence are equivalent; exclusivity alone is insufficient. Exchange and swap transfers keep their internal empty state unobservable and invoke no user code. A payload swap is O(payload size), not an automatic handle swap. Redundant copies, scratch storage and cleanup are eliminated only while preserving the checked update plan (§15.7).

**Byte transfers.** Every currently storable complete value supports an authorized Copy or Move by transferring its `size` bytes, without user calls, allocation, count increments or pointer fix-ups. A Copy preserves the source's responsibility and a Move transfers it. For a nonzero size, the source range and the uninitialized destination range must not overlap. Moving an object handle leaves the object body, and pointers to it, in place. A future address-dependent value representation must revise this contract and the metadata schema rather than silently requiring relocation callbacks. Padding and unused environment bytes are transferable but are neither semantic values nor automatically defined ABI-coercion bits.

For a contiguous range relocation, the checked `count * stride` bytes may use a memmove-equivalent transfer under exclusive move authority. Overwritten responsibility is disposed of before relocation, exactly one responsibility per logical element is preserved under overlap, and no intermediate partially moved state is exposed. Zero-sized elements keep their logical transfer and cleanup effects.

Direct Field and Scalar operations are preferred, and slots created only to use a memory intrinsic are avoided. For valid transfers, nonvolatile `llvm.memcpy` is preferred on nonoverlapping ranges, `llvm.memmove` on authorized overlapping ranges, and `llvm.memset` for required filling, preserving alignment and constant lengths. These operations implement already-authorized acquisition; they grant no Copy or Move and no whole-value read of partial data. Zero-byte instructions may disappear while state updates and destruction remain. LLVM chooses expansion, vectorization and libcalls; `memcpy.inline` is reserved for constant-length sites that must not call a library function. Metadata range destruction follows §21.2.2; destroying a value and freeing its enclosing storage are separate operations, and the free happens only after normal destruction.

```llvm
declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1 immarg)
; Inside a function: valid, nonoverlapping 24-byte ranges.
call void @llvm.memcpy.p0.p0.i64(ptr align 8 %dst, ptr align 8 %src, i64 24, i1 false)
```

### 21.4.6. Generic scratch storage and fixed frames

#### 21.4.6.1. Reuse and lifetime

Direct result construction, reuse of acquired owned-argument storage and removal of intermediate transfers are preferred, under the evaluation, alias, Loan, storage-identity and cleanup conditions of §21.4.4. A Copy source is kept separate from the callee's acquired value, and a live replacement target is never written early.

After an owned argument has been Moved, its storage may be reused only when no old responsibility or reference remains and the capacity and alignment fit; this does not permit assignment to an immutable source parameter. Values with overlapping lifetimes, including cleanup and final references, need distinct regions. Loop temporaries may reuse a region across nonoverlapping iterations without moving destruction to the last read.

#### 21.4.6.2. Exact entry reservations

A temporary whose size and alignment are fixed in a body may use that body's static frame. The remaining storage is laid out for each closed substitution, and its entry reserves the **exact required capacity**, rounded up to the maximum required alignment, using a fixed-size entry `alloca`. Another instance's maximum and capacity buckets are never used.

Per-call scratch is passed to the shared body as a separate internal pointer argument, with offsets supplied as Fixed facts or integer context slots. Storage required for ABI adaptation, such as materialized scalar arguments and result slots, is included in the plan.

~~~text
directEntry(x: i32, ...) -> i32:
    reserve fixed argument/result adaptation storage as needed
    scratch = fixed alloca(capacity, alignment)  // omit when capacity is zero
    sharedBody(adaptedArguments, resultStorage, privateContext, scratch)
    return the result after normal cleanup completion

sharedBody(..., context, scratch):
    temporary = scratch + context[offsetSlot]
    construct, use and clean it according to the verified plan
~~~

Every offset, capacity and alignment is validated statically against the entry's reservation. The shared body performs no runtime scratch layout and no dynamic scratch allocation; ordinary source-required heap allocation keeps its existing contract. Loops do not accumulate per-iteration stack allocations, and recursive invocations have independent live storage.

Entries are shared only when the entry ABI, capacity and alignment, destination body, adaptation, and all embedded constants and context references agree. A capacity of zero removes the allocation, but ABI or context differences may still require an adapter.

Scratch is never stored in a shared context, and no borrow into it may escape in a result or Closure; moving its values out, including values containing legal references to external storage, remains allowed. Normal return releases the frame without duplicate destruction, and a tail call that discards the frame while scratch is still in use is prohibited.

#### 21.4.6.3. Initial profile boundary

The initial fixed-stack path supports alignment of at most 16. A Type's alignment is never reduced; an unsupported representation is diagnosed when no valid path exists. Required Windows stack probes and unwind information are generated for fixed frames.

Zero-sized values use the aligned substitute storage of §21.2.4 while keeping their state, Loans and destruction counts; substitute addresses prove neither positive `dereferenceable` ranges nor `noalias`. Exact capacity describes the planned reservation, not the final machine frame including spills and call areas.

Dynamic `alloca`, a heap fallback for scratch, and new stack-exhaustion or allocation-failure contracts are not introduced. The existing Library output boundary is preserved, and unknown substitutions are outside this generation scope.

## 21.5. LLVM Windows x64 profile

### 21.5.1. Target and optimization

The initial profile is **windows-x64-v1**: LLVM **22.1.8**, target **x86_64-pc-windows-msvc**, CPU **x86-64**, features **+sse2**, relocation model **pic**, code model **small**, and asynchronous unwind tables. It uses the target's verified DataLayout (§21.1). Extra features are not inferred from the build machine, AVX is not required, and absolute 32-bit data references are not compensated with `/FIXED` or a low image base. Static artifacts outside the code-model range are diagnosed separately from heap size limits.

Every generated function definition, including the entry and runtime functions, carries matching `target-cpu`, `target-features`, `denormal-fp-math=ieee,ieee` and `uwtable(async)` (LLVM `uwtable` is equivalent). The module emits:

```llvm
target triple = "x86_64-pc-windows-msvc"
; Also emit the verified target datalayout.
!llvm.module.flags = !{!0}
!0 = !{i32 8, !"PIC Level", i32 2}
```

The compiler emits pre-optimization IR. O0 skips the general IR optimization pipeline and uses `llc -O0`; the default O2 uses `opt default<O2>` followed by `llc -O2`. `opt` reads CPU and features from function attributes, so they are not duplicated as `opt -mcpu/-mattr` options. `llc` and manifest settings match, and IR is verified before and after optimization.

LLVM performs inlining, constant propagation, dead-code elimination, SROA, mem2reg, instruction selection and register allocation. No default O3, unconditional `alwaysinline`, loop unrolling or redundant general SSA optimizer is required. Internal ABI changes by whole-module optimization are allowed only when all uses and semantics stay consistent, and the external ABI and observable storage are preserved. O0 and O2 cannot change acceptance, checks, cleanup or nontermination.

### 21.5.2. Module, symbols, and caches

One project and target emits one `.ll` containing target information, private constants, required external declarations, internal runtime/Kimi/user/cleanup definitions and, for an Application, the entry (§22.2). Source public visibility is not native export.

| Symbol | Linkage |
| --- | --- |
| User functions, public `main`, implicit body, Kimi, cleanup, runtime helpers | Internal definitions in both output kinds |
| Application `__kimi_start` | External definition; absent in a Library |
| Windows APIs / LibraryImport | External declarations; `dllimport` follows the library kind (§20.8.2) |
| Backend marker `_fltused` | Strong external data definition (§21.5.7) |
| Internal constants | Private; literal sharing follows §21.5.6 |

Each generated function has one definition and no same-name `declare`; explanatory signature-only fragments are not complete modules. Source names are mangled with the Kotonoha and declaration identity, arguments and implementation selection.

One final-module symbol table is used. Same-named external declarations are shared only if their physical Type, calling convention, ABI attributes, `dllimport` and actual provider all agree under §20.8.2, regardless of consumer aliases. Function/data collisions and declaration/generated-definition collisions are errors. `__kimi_` is reserved for compiler internals, `llvm.` for LLVM, and `_fltused`, `__chkstk`, `memcmp`, `memcpy`, `memmove` and `memset` for profile supplies; these external names are rejected in user LibraryImport. Exact ABI symbol names are matched. LLVM identifiers and UTF-8 bytes are quoted and escaped, and raw source strings are never inserted into IR. Shared `ptr` signatures do not merge source unsafe contracts; only guarantees true for every use are attached.

Deterministic generation records keep the compiler, layout and generation-scheme identities, entry and body ABI contracts, target/DataLayout/codegen settings, backend package version and hash, selected fragments and generated sources, complete arguments and selected implementations, cleanup, callees and helper dependencies. Content identities are validated without requiring a public internal ABI version. Under §21.3.4, persistent semantic plans validate their own dependencies, while generation records and code are rebuilt under the current scheme, not restored from an object cache. Size and alignment alone are never a sufficient key. Generation does not depend on absolute working directories, host locale, enumeration order or host CPU.

### 21.5.3. Checked instructions and raw pointers

Existing arithmetic, conversion, indexing and failure order are preserved; `nsw`/`nuw`, `poison` or undefined behavior cannot implement a required Abort check.

| Operation | Initial lowering |
| --- | --- |
| Integer add/subtract/multiply | Signed or unsigned overflow intrinsic, or an equivalent result-plus-overflow check; Abort on failure |
| Negation, increment/decrement, compound assignment | Check first; commit the write only on success |
| Integer division/remainder | Check for a zero divisor and for signed minimum / −1 before the instruction |
| Shift | Check the count in its original Type, `0 <= count < left width`, then convert; `ashr` for signed right shift, `lshr` for unsigned, `shl` without treating discarded bits as arithmetic overflow |
| Integer conversion | Check the destination range before extension or truncation |
| Float to integer | The ordered range checks below, then `fptosi`/`fptoui` only on success |
| Integer to float / float width conversion | The required rounding; detect finite-to-infinity failure |
| Array/index/Range | The required bounds checks before a successful address calculation |

Constant evaluation follows §17.3.4. A check is removed only after its success is proven; folding a failing path to Abort, or removing an unreachable path, is allowed.

```llvm
declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
; Inside a function:
%pair = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %a, i32 %b)
%sum = extractvalue { i32, i1 } %pair, 0
%overflow = extractvalue { i32, i1 } %pair, 1
br i1 %overflow, label %abort_overflow, label %success
; abort_overflow calls the generated noreturn Abort helper with the source site.
```

**Float-to-integer bounds.** For source precision `p` (24 for `f32`, 53 for `f64`) and destination width `N` = 8, 16, 32 or 64 (64 for `isize`/`usize`), `x` is tested directly:

| Destination | Lower bound | Upper bound |
| --- | --- | --- |
| `iN`, `N <= p` | `x > -2^(N-1) - 1` | `x < 2^(N-1)` |
| `iN`, `N > p` | `x >= -2^(N-1)` | `x < 2^(N-1)` |
| `uN` | `x > -1` | `x < 2^N` |

All constants are exact in the source format, and the ordered comparisons reject NaN and infinities. Only then does `fptosi`/`fptoui` execute, truncating toward zero. No `trunc`/`truncf` helper or `llvm.trunc` is needed, and the conversion is never performed speculatively with its `poison` hidden by `select`. Thus −128.75 to `i8` yields −128 and −0.75 to `u8` yields 0, while −1 to `u8` and `f32` 2147483648 to `i32` Abort.

**128-bit subset.** Storage and acquisition, internal arguments and results, comparisons, bit operations, shifts, integer conversions, and checked add, subtract, negate, increment, decrement and multiply are supported, subject to verification that LLVM 22.1.8 needs no unsupplied helper. `i128`/`u128` division and remainder, and conversions in both directions with `f32`/`f64`, including in compound assignments, are diagnosed before optimization, even in unused bodies and branches. Required constant evaluation and fitted constant storage are separate. `__divti3`, `__udivti3`, `__modti3`, `__umodti3` and the `__fix*ti`/`__float*ti*` helpers are not supplied initially. Actual multiplication expansion is verified rather than assuming a helper from its name.

**Raw pointer operations.** Allowed pointer-Type casts use the same address-space-0 `ptr`; same-Type equality and null tests use `icmp eq`/`ne`; `usize` conversions use `ptrtoint` to `i64` and `inttoptr` from `i64`. `p + n` uses a storage-Type GEP with an `i64` index, and `p - n` uses `sub i64 0, n`; the initial form has no `inbounds` or `nsw`/`nuw`. GEP spacing is checked against the positive `stride(T)`. Zero displacement preserves null as well as other pointers. Arithmetic alone proves neither alignment nor initialization. These operations keep the unsafe allocation, provenance, mathematical-displacement and no-wrap conditions of Chapter 5, and violations need not Abort. Integer reconstruction creates no extra dereference permission, and typed access still needs a valid range, alignment, permissions, initialization and replacement legality. Runtime buffer arithmetic has its own checked contract (§22.5.3).

For proven complete payload targets, the ordinary value-borrow address and load/store/transfer rules apply. Same-concrete-Type tests may be folded only while preserving operand evaluation, acquisition, Loans and Origins. Content destruction does not end the containing allocation's lifetime, and raw pointer optimization cannot erase required dependency checks (§15.7.3).

### 21.5.4. Floating-point environment

Ordinary `fadd`/`fsub`/`fmul`/`fdiv`/`fneg`/`fcmp` and conversions are used, without fast-math, reassociation, approximation or FP fusion. The NaN, infinity, signed-zero, subnormal, rounding and Equatable distinctions of Chapter 13 are preserved. `denormal-fp-math` is `ieee,ieee`, including any `f32` override.

The Windows x64 ABI-standard MXCSR control state is required: round to nearest, ties to even; DAZ off; FTZ off; hardware FP exceptions masked. This is the startup and foreign-code connection contract, also assumed by inspection-only Library IR. External code that temporarily changes control bits restores them before normal return, and deliberate environment-changing APIs are unsupported; external initialization follows the same condition. No startup MXCSR setter, per-call save/restore adapter, individual preservation flag, constrained intrinsic, `strictfp` or FP-environment `noinline` is generated.

MXCSR status bits are volatile and not exposed; external code must not depend on status flags left by Kimigayo calculations. Violating these boundary conditions gives no result or cleanup guarantee and no automatic repair. Future export, callback, thread entry, dynamic rounding and exception observation require new contracts. Normal FP optimization may preserve language results and explicit Abort checks without preserving hardware exception status.

### 21.5.5. Storage, attributes, and unwind information

Address-free Scalar values stay in SSA, using `phi` at joins, and mutable locals may use promotable fixed slots. Required fixed-size `alloca` instructions are placed in the function entry, before calls, with explicit alignment, while initialization and cleanup stay at their source execution points. Dynamic stack allocation is initially unsupported. Storage is reused only after the old destruction responsibility and final references have ended; a completed Move may discharge the old responsibility without destruction. Generic entry reservations follow §21.4.6. Optional `lifetime.start`/`lifetime.end` markers follow actual storage lifetime, not an Origin spelling or a Move alone, and unproven markers are omitted.

**Safe value-borrow call contract.** `ref/V` and `uniq/V` protection is verified from receiver or argument formation through the entire call, extended by result uses where required. Eligible exclusive preparation uses the reservation permissions of §15.6.7; full exclusivity is proved at activation before logical callee entry. Preparation helpers must respect reservation permissions and cannot receive a reserved value as an active exclusive argument. Overlaps among arguments, captures and storage anchors are checked, including all direct and indirect static effects, defaults, initialization, cleanup and reentry (§15.6.4). Caller protection is never shortened to the callee's last use, and unknown potentially conflicting effects cause rejection. Owned environments still keep their relevant static and capture anchors.

`ref/V` keeps `V`'s immediate inline storage unchanged throughout the call. `uniq/V` permits no conflicting independent access, while access through its derived child Reborrows remains valid. This common call-wide proof supports `noalias` for **both** borrow modes, applied consistently on definitions, calls and adapters with pointer provenance preserved; no separate private-body reproof per call is needed. Passing the same shared reference twice is legal, because the modified-memory premise of `noalias` still holds.

| Attribute on a normal safe value-borrow pointer | Required premise |
| --- | --- |
| `nonnull`, `noundef` | A valid initialized borrow pointer |
| `align` | A proven constant alignment, or a known lower bound for a generic `V` |
| `dereferenceable` | A positive constant valid byte range; never inferred from zero-size substitute storage |
| `readonly` for `ref/V` | No writes to `V`'s inline storage through the argument or derived pointers; this is not function purity |
| `noalias` for `ref/V` and `uniq/V` | The complete call-wide contract above |

These guarantees cover the immediate `V` storage only. Targets reached through loaded pointers keep their own rights: another `rc` handle may share the payload and update counts without writing the borrowed handle slot. Array and Slice handle `noalias` does not prove their element pointers disjoint; vectorization needs separate provenance, range and Loan proofs. This table does not apply to object-borrow headers, uninitialized internal pointers or an entire `environmentWord`.

Unsafe and FFI implementations receiving these borrows must satisfy the same promises: raw pointers derived from `ref` cannot write its inline storage, and `uniq` permits no conflicting independent access during the call. Missing Loan or effect verification is diagnosed before emission. Future interior mutability or concurrency requires revisiting the shared proof.

For other optimization attributes, defined bits and absence of `poison` are proven separately for `noundef`, including padding in coercions; the full GEP conditions for `inbounds`; and the absence of signed or unsigned overflow for `nsw`/`nuw`. `mustprogress`, `willreturn` and `loop.mustprogress` are not applied uniformly to ordinary functions or loops.

All generated definitions use `uwtable(async)`. LLVM produces the required `.pdata`/`.xdata`, prologue/epilogue and register/stack recovery information, including decisions for optimized leaf functions. This enables OS stack recovery and walking, not language exceptions, Abort cleanup or foreign unwind permission. `nounwind` is treated separately: the initial emitter does not infer it merely from LibraryImport's no-unwind contract and generates no exception-catching `landingpad`. Native assembly needs appropriate Windows unwind information for its own stack and nonvolatile-register operations.

### 21.5.6. Constants

Exact integer magnitudes and decimal values are kept through fitting; a floating value is rounded once to the selected `f32`/`f64`, and its fitted bits are emitted using exact LLVM 22.1.8 syntax. Values are never rounded through host `f64` or culture-dependent formatting, and bit equality is verified after LLVM rereads the IR (for example, 0.1 fitted to `f32` has bits `0x3DCCCCCD`).

For integer constants, the fitted value is validated against its language Type before lowering, and the selected-width bit representation before LLVM serialization. LLVM parser acceptance is not proof that a constant was in range, and emission must not rely on implicit truncation to repair an invalid constant plan.

After source newline and escape processing, strings are encoded as UTF-8 with byte lengths; an embedded NUL is data, and no terminator is added automatically. Interpolation evaluation order is preserved. Identical byte sequences are shared within the module as private `unnamed_addr` constants; backing addresses define neither string equality nor identity. An empty literal is Static, null and length zero, with no allocation. Non-Copy ownership is unchanged (§22.5.5).

```llvm
; UTF-8 for U+3042; length 3, no trailing NUL.
@kimi_text_a = private unnamed_addr constant [3 x i8] c"\E3\81\82", align 1
```

### 21.5.7. Backend support supply

Toolchain storage, native generation and verification, adopted-archive installation and the Application build and reference flow are defined together in §20.8.8.

Memory intrinsics may become external libcalls. `memcmp`, `memcpy`, `memmove`, `memset` and the Windows x64 assembly `__chkstk` are supplied by the versioned native COFF static archive **kimi_backend_windows_x64_v1.lib**, logical name **kimi_backend**. Their loop bodies are not generated in `.ll`, and no bitcode or LTO members are used; they stay outside O2 to avoid recursive libcalls, and `optnone` alone is insufficient. One strong symbol per archive member is preferred.

The memory helpers meet the Windows x64 C argument and result contracts, including both overlap directions for `memmove`. `__chkstk` uses the backend's special ABI, not an ordinary C function: the RAX size input and its preservation, stack and register restoration, and guard-page probing are validated against emitted calls. Required probes are never disabled, and a similarly named function from another ABI is never substituted. All helpers meet this profile's CPU, MXCSR and unwind contracts, with no extra external dependencies, CRT startup or dynamic initialization.

`memcmp(const void*, const void*, size_t) -> int` compares exactly the requested byte range as unsigned bytes and returns a negative, zero or positive value according to the first difference. It performs no writes or allocation and never reads beyond either range. This supplied implementation accepts a zero count without accessing either pointer, including null. String equality compares lengths first; string ordering compares the common byte prefix and then the lengths. The release tag and padding are not comparison data. Backend supply ABI 2 adds `memcmp`; the archive filename keeps the windows-x64-v1 profile name, while the ABI and content identity are checked separately.

`ssp`/`sspstrong`/`sspreq` are not emitted initially. Stack-protector cookies, failure handling and other arithmetic helpers require separately validated supplies before dependent features are enabled.

Every Application and Library module, regardless of FP use or optimization, supplies exactly one strong external data definition:

```llvm
@_fltused = global i32 0, align 4
```

This backend marker is not CRT initialization state. No `dllimport`, weak or common definition is allowed, and no other input, including the backend archive, may define it.

The compiler's profile catalog fixes `packageId=kimi-backend-windows-x64`, `abiVersion=2`, the actual archive's SHA-256, the profile/LLVM/CPU/FP/unwind contract, and `providedSymbols=[__chkstk, memcmp, memcpy, memmove, memset]`. `packageVersion` comes from the shared compiler and backend release in `Directory.Build.props` (§20.8.7). The ABI version is updated when symbol or call contracts change. Filename or path equality is insufficient, and if version, ABI and hash are not established, no successful generation is claimed with a placeholder supply.

The archive is recorded as a profile-wide link input even when no known reference currently needs it; unused archive members need not be linked. Generated `_fltused` and externally supplied symbols have separate manifest classifications (§20.8.3). Later LLVM versions may add or remove references, so actual object undefined symbols are inspected and their providers verified. Unknown or unsupplied dependencies fail adoption or linking and never receive empty stub helpers. This supply does not add to the six runtime operations or the seven Windows APIs.
