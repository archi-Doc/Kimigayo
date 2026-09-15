# 21. Layout, runtime metadata, and code generation

[Specification index](../SPEC.md)

Physical representation and code sharing must preserve Type identity, ownership, evaluation and cleanup. This chapter separates language-wide requirements, Windows storage contracts and compiler-controlled function passing.

The Windows storage and metadata contracts below are normative for this profile. They retain §7.6's capture/call restrictions and §21.4.2's compiler-controlled function passing. Specification adoption does not establish implementation coverage (STATUS.md).

## 21.1. Structure layout and ABI

Storage layout, valid value representation, and function ABI are separate contracts. A layout Attribute selects storage rules; it does not select a calling convention, grant Copy, or make a Type safe for foreign construction. The initial physical rules below belong to the versioned [Windows profile](#215-llvm-windows-x64-profile), not a stable cross-version ABI.

### 21.1.1. Common layout rules

For a storable Type T, size(T) reserves inline bytes including padding, alignment(T) is a positive power of two, and stride(T) is size rounded up to alignment (zero when size is zero). These are specification notation, not source operators. Check rounding, addition, and multiplication for overflow. In windows-x64-v1, size, stride, and valid Field offsets cannot exceed 2^63 - 1; diagnose any stricter backend limit with its reason. A representable size does not guarantee successful allocation.

Fixed arrays have size and stride N * stride(T), element offset i * stride(T), and alignment(T), including N = 0. Unit has size 0, alignment 1, stride 0. Zero-sized values retain evaluation, initialization, ownership, Loans, and destruction. Shared addresses do not merge logical Places. Positive-sized components cannot overlap; nested padding cannot be reused by outer Fields. Padding has no guaranteed content, even in an initialized value; permitted byte transfers may include it, but typed reads, comparisons, and integer coercions must not treat it as a value.

Layout uses the selected, merged storage declarations after Mods, Type substitution, and storage classification. A struct contains its own Fields and its direct inline base, if any; computed members add no storage, and custom stored accessors do not change slot count. Reject infinite inline storage cycles; borrows, raw pointers, and object handles do not embed their referents. Reordering cannot change Field Identity, logical initialization order, Partial Move, Loans, or destruction order. Identical compiler, target, settings, and selected inputs must reproduce layout.

### 21.1.2. Layout Attribute and fragments

`#Layout("Kimigayo")` and `#Layout("C")` are compiler-recognized Attributes on struct declarations only. Accept exactly one unnamed, non-interpolated string literal, matched case-sensitively; a trailing comma is allowed. Diagnose missing/extra/named arguments, unknown modes, wrong targets, and repeated Attributes on one fragment, even with equal values. No `#Repr` alternative or layout-query syntax is introduced.

An omitted Attribute means unspecified during fragment merging. Share the explicit mode across selected fragments; equal specifications on different fragments are allowed, conflicting ones are errors. With none, choose Kimigayo. Explicit Kimigayo and omission create no distinct Type Identity.

C layout requires all selected instance Fields, including generated Fields, to occur in one fragment in written order. Other fragments may add methods, computed members, and other declarations without storage; the Attribute need not occur on the storage-bearing fragment. Kimigayo layout uses §20.7.4's logical order. Neither uses filesystem enumeration or Binding order. Finalization follows Mods and selection; storage cannot be appended afterward.

A generic struct shares its mode across instantiations, but each concrete Type has its own size, alignment, offsets, and formation checks. Retain unresolved layout obligations until the required instantiation; do not infer equal layout or code sharing from equal modes.

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

**Kimigayo.** Each Field/base meets its natural alignment; the aggregate alignment is at least their maximum. Reserved component ranges fit inside the struct. The profile algorithm below fixes its layout; it promises no target-independent offsets, globally minimum size, C compatibility or stable ABI across compiler/input changes. This is Kimigayo's own contract, not rustc layout or Rust ABI.

The Windows algorithm reserves the complete direct base at offset zero, then places own Fields in **descending natural alignment**, breaking ties by logical order. Align each component and round the total to the maximum alignment with checked arithmetic. Size equals stride. Never reuse base or nested tail padding. If all components are zero-sized, size/stride are zero and alignment is their maximum, or 1 when empty.

Use the same alignment-sorted algorithm for concrete Closure captures, Tuple elements and each enum Case payload. It does not reorder C fields, array positions, the base or enum tag. Keep logical initialization, access identities, Partial Move and reverse destruction order unchanged at every optimization level. Generic offsets depend on the whole instantiated layout, including fields with fixed declared Types.

```text
Logical components: a: u8, b: u64, c: u8, d: u64
Physical order:     b @ 0, d @ 8, a @ 16, c @ 17
Size/stride: 24; alignment: 8 (rather than 32 in logical order).
Acquisition: a, b, c, d; destruction: d, c, b, a.
```

**C.** The initial contract is Windows x64 MSVC layout with effective packing 16 (/Zp16), without pragma pack, explicit alignment, or bit-fields. Do not inherit host packing settings. Nested Types keep their own modes. Require natural Field alignment at most 16; larger alignment is unsupported, not silently reduced.

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

Reject C layout on open or derived structs, empty structs, direct zero-sized Fields (including Unit and zero-length arrays), and multiple storage-bearing fragments. Methods, constructors, computed members, and deinit do not themselves prevent C layout; foreign construction/destruction is a separate contract. C layout describes the owned payload, not an object handle, allocation header, or reference count.

NativeRecord above has offsets 0, 8, 16, size/stride 24, and alignment 8:

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

Use ordinary LLVM structs, not packed `<{ ... }>`, for C layout. Verify allocation size, ABI alignment, and offsets against TypeLayout. Source Field index, LLVM element index, and byte offset are distinct. C mode alone does not define native exports, mangling, DLL compatibility, serialization, or aggregate argument passing.

### 21.1.4. Initial scalar representation

windows-x64-v1 is little endian, uses address space 0, and has 64-bit pointers. Validate this table against LLVM 22.1.8's target DataLayout. LLVM integer Types carry width, not signedness; select signed/unsigned operations from the language Type.

| Language Type | LLVM storage | LLVM computation | Size / alignment / stride, bytes |
| --- | --- | --- | --- |
| i8 / u8 | i8 | i8 | 1 / 1 / 1 |
| i16 / u16 | i16 | i16 | 2 / 2 / 2 |
| i32 / u32 | i32 | i32 | 4 / 4 / 4 |
| i64 / u64, isize / usize | i64 | i64 | 8 / 8 / 8 |
| i128 / u128 | i128 | i128 | 16 / 16 / 16 |
| f32 / f64 | float / double | float / double | 4 / 4 / 4; 8 / 8 / 8 |
| bool | i8 | i1 | 1 / 1 / 1 |
| char | i32 | i32 | 4 / 4 / 4 |
| unsafe/T | ptr | ptr | 8 / 8 / 8 |
| Unit | May be omitted | No ordinary value | 0 / 1 / 0 |

Valid stored bool bytes are only 0 and 1. Under that validity premise, load i8 and truncate to i1; store by zero-extending i1 to i8. This does not sanitize invalid bytes. Windows BOOL is i32 and converts by comparison with zero. char stores an unsigned Unicode scalar value: surrogates and values above U+10FFFF are invalid. Neither representation establishes compatibility with C char or WCHAR.

This table does not authorize all operations or FFI uses. Safe borrow and object-handle storage follows §21.2; equal pointer representation does not grant raw-pointer permissions. Heap alignment support is separately limited to 16 (§22.5.2); never round down a Type's alignment.

### 21.1.5. Tuple and enum layout

Tuples use §21.1.3's alignment-sorted aggregate algorithm, keeping logical indices and evaluation/destruction order separate from physical offsets. All-zero-sized elements yield size/stride zero. Tuple has no Layout Attribute or C ABI. For example, (u8, u64) has logical-element offsets 8 and 0, size/stride 16, alignment 8.

Initial enums use an explicit i32 tag and an aligned payload area, without niche optimization. Number selected Cases from zero in declaration order; more than 2^32 Cases is unsupported. These internal tags introduce no source discriminants or integer conversion.

Lay out each Case payload using §21.1.3's alignment-sorted algorithm, without moving the enum tag. Let A be the maximum payload alignment and P the maximum payload size rounded up to A; if all payloads are empty, use A = 1, P = 0. The payload starts at alignUp(4, A); enum alignment is max(4, A), and total size/stride is rounded to that alignment. Only a selected tag paired with its valid active payload is a valid enum value. Initialization, Move, match, and cleanup use that active Case, never unused payload bytes.

Preserve payload alignment in LLVM with a zero-length alignment carrier followed by P bytes: use [0 x i8/i16/i32/i64/i128] for alignment 1/2/4/8/16. The carrier is not a language Field or cleanup target.

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

Verify these layouts when nested in structs/arrays. Do not flatten payloads to alignment-1 byte arrays, expose tag-only mutation, or claim C enum/union compatibility.

### 21.1.6. C exchange eligibility

C layout and C-exchangeable storage are different judgments. Initially allow owned i8/u8 through i64/u64, owned f32/f64, raw unsafe/T pointers, positive-length fixed arrays of eligible elements, and owned C-layout structs whose Fields are recursively eligible.

Exclude Kimigayo-layout structs, Tuples, enums, string, dynamic collections, bool, char, i128/u128, isize/usize, safe borrows, object handles, and function/closure values until their C correspondence is specified. A C-layout struct may contain string if layout succeeds, but it is not C-exchangeable. For example, C-layout Pair<i32> and Pair<u64> have different layouts; Pair<()> violates the zero-sized Field restriction, and Pair<string> gains no marshalling.

A pointer guarantees its value representation only; its pointee may remain opaque. Foreign reads/writes require a separate pointee-layout and validity contract. Foreign construction/overwrite for receipt by Kimigayo initially also requires no user deinit recursively. Constructors, lifetimes, ownership transfer, active Loans, and accessor bypass still require the unsafe contract. No automatic Copy capability, raw-storage initialization API, or safe-to-raw conversion follows.

For ABI comparison, retain target ABI, recursively eligible Field Types, merged order/content, and layout options. Aggregate arguments/results remain excluded from LibraryImport (§22.3), even when storage is C-exchangeable. Packed/transparent layouts, explicit alignment/offsets, unions, bit-fields, flexible array members, external enum representations, and public layout queries remain extensions.

## 21.2. Runtime representations and metadata

### 21.2.1. Type identity and descriptors

Every live object can reach immutable metadata for its Dynamic Type, shareable among objects of that concrete Type. Required logical information is:

```text
Type Descriptor
    ├─ Runtime Type Identity
    ├─ relationships required by defined Supports operations
    ├─ complete dynamic destruction operation
    └─ layout or receiver-adjustment information where required
```

**Runtime Object Type Identity**, also called Runtime Type Identity here, identifies the actual concrete payload Core D. Use these logical functions on Types whose necessary formation checks have succeeded:

```text
N(A)      = normalize transparent aliases, resolved associated-Type projections,
            grouping, and redundant owner prefixes
ArgKey(A) = remove every Origin from N(A), recursively retaining Type structure
            and Semantics; nominal nodes retain Symbol/Kotonoha/version and
            their ordered argument keys
CoreId(D) = the concrete Core's identity computed by the same rules
```

An object handle's Runtime Type Identity is `CoreId(D)`, excluding its root `obj/rc/arc/objref/objuniq`. Do not apply this root-handle removal recursively inside generic arguments: their Object Semantics remain in ArgKey. Preserve Function/Tuple structure and generated Closure declaration identity. A static base or Contract View Target is not a substitute for the actual D.

| Comparison, assuming valid Types | Runtime Type Identity |
| --- | --- |
| `obj/D` and `rc/D` for the same actual D | Equal |
| `Box<ref/i32 from a>` and `Box<ref/i32 from b>` | Equal |
| `Box<ref/i32>` and `Box<i32>` | Different |
| `Box<objref/C>` and `Box<obj/C>` | Different: inner Semantics are retained |
| `obj/Box<ref/i32 from a>` and `obj/Box<i32>` | Different after root-handle removal |

Runtime identity equality does not imply equal value representation, layout, ABI, ownership operations, assignment compatibility, Origins, or Loans. It neither authorizes code sharing nor skips validation. Equal names, layouts, member sets, or descriptor addresses alone also do not define identity. Fixed-array Type structure retains the evaluated length and element-Type key. General Const arguments beyond function lengths are not introduced. Other identity and key purposes are separated under [generation keys](#2132-identity-and-generation-keys).

Declared base and conformance relationships are fixed by validated definitions; no unrelated extension, module search, or runtime registration changes them. Revalidate dependent artifacts under §21.3.4 before generating metadata; their identities, bases, and verified mappings must agree, independently of load order. Receiver adjustment cannot bypass public ObjectCompatible, access, Type, Origin, or Loan checks. ObjectCompatible is compile-time interface information, not a required Descriptor field. A destruction entry does not make deinit a source-level function value.

An extension that introduces runtime implementation selection defines its own additional selection information. The current descriptor adds no runtime member-dispatch slots; its Windows storage is specified below, independently of function ABI.

### 21.2.2. Value metadata and object descriptors

The following immutable, module-local records belong to windows-x64-v1. They define storage and entry contracts, not a stable external calling convention; all physical signatures use §21.4.2's FunctionAbi. Instance counts, allocation state, initialization state and Loans do not belong in shared metadata.

**Type keys.** Value metadata uses ArgKey of the full normalized Type, recursively erasing Origins only and retaining all Semantics. Object payload metadata uses CoreId(D) of the complete Dynamic Type D; this equals ArgKey(owner/D). It is independent of the handle mode and static View Target. Assign distinct nonzero u64 tokens deterministically within the final generation unit. Check hash collisions against original keys. Artifacts retain those keys and dependencies; integration may retokenize all references. Tokens have no public numeric, persistence or dynamic-linking contract. Multiple records for one key are allowed; shared code/layout does not merge Type identities, and equal tokens prove no static Type, Origin, Loan or code-sharing judgment.

**ValueMetadata: 48 bytes, alignment 8.**

| Offset | Field | Contract |
| --- | --- | --- |
| 0 | typeKey: u64 | Nonzero full-value Type token |
| 8 | size: u64 | Inline size including tail padding; already aligned, hence also stride |
| 16 | alignment: u64 | Positive power of two, equal to TypeLayout alignment |
| 24 | flags: u64 | Bit 0 is HasCopy; all other bits zero |
| 32 | destroyValues: ptr | Null exactly when all values of the full Type need no runtime destruction |
| 40 | typeContext: ptr | Immutable required Type information; null when unnecessary |

There is no separate stride or Copy/Move entry. All current Copy Types have no user deinit and recursively cleanup-free components, so HasCopy implies null destroyValues; the converse is false. Zero size and the current enum Case do not prove the full Type cleanup-free. Never has no value metadata, although its static identity and constraints remain meaningful.

`destroyValues(first, count, metadata, location)` is the sole metadata destruction entry; one value uses count = 1. For count = 0 or a null entry, skip the call and address calculation. Otherwise first is nonnull, aligned and live, pointing to count complete initialized values at metadata.size stride. The caller checks count/range/offset arithmetic and excludes moved, partial and spare storage. Destroy in reverse logical order, count - 1 through zero; a zero stride still requires every logical destruction. Complete each value's deinit and component cleanup before the preceding value. Abort or nontermination stops subsequent work. This entry destroys values but never frees the enclosing storage.

Partial cleanup may batch only contiguous complete segments compatible with the required logical order. Dictionary reverse-insertion value/key cleanup is not an address sort; use separate calls, including count = 1, where needed. A range of strings can use one indirect entry and a reverse loop, without eliminating each value's required free or nested dynamic dispatch.

TypeContext supplies everything the entry needs independently of the original caller's stack/context: component/Case metadata, selected operation and specialization callees with their contexts, or equivalent information embedded in code. If a Type operation needs offsets, provide a logical-element map from the **whole instantiated layout**, including Fields whose declared Types were already fixed. Omit unused maps. Its reader and producer agree on the schema; no instance length, partial state, Origin or Loan is stored here. Fixed-array TypeContext retains elementMetadata and N. Ordinary shared-body layout and length reads use direct GenericContext slots (§21.3.3), derived from the same TypeLayout.

**ObjectDescriptor: 24 bytes, alignment 8.** Share it across obj/rc/arc objects of the same complete Dynamic Type and layout.

| Offset | Field | Contract |
| --- | --- | --- |
| 0 | payloadMetadata: ptr | Nonnull ValueMetadata for complete D; typeKey is CoreId(D) |
| 8 | freeStorage: ptr | Nonnull operation releasing the original object allocation only |
| 16 | viewMap: ptr | Nullable verified Supports/base/receiver-adjustment information |

viewMap introduces no new runtime method dispatch. The logical free entry is `freeStorage(header, descriptor, location)`; it performs no value destruction, count operation or side-table release. The fixed header in §21.2.3 makes the complete payload address header + 16 without a descriptor load. Allocation size is checked 16 + payloadMetadata.size, constant when concrete and otherwise checked at runtime. Free needs no size argument. Do not duplicate allocation size, payload offset or allocation alignment in the descriptor; payload alignment remains in ValueMetadata. Metadata for rc/D describes a handle and cannot replace metadata for payload D.

Each view must reach original identity, valid receiver/cast adjustments and complete dynamic destruction/storage release while preserving Origins/Loans. Descriptor addresses are not Type identity. Dynamic Array/Dictionary and Slice still require their own complete storage/element-state plans before emission; §4.7 does not prescribe their ABI. The value-borrow and callable layouts below are now specified, with no implicit unsupported one-pointer fallback.

### 21.2.3. Windows x64 object and weak profile

#### 21.2.3.1. Storage and count representation

All allocations use §22.5.2's 16-byte-aligned allocator. These are internal storage contracts, not FFI or cross-version ABI guarantees.

| Value/storage | Fields or pointer target | Size / alignment, bytes |
| --- | --- | --- |
| obj/rc/arc/objref/objuniq | Nonnull pointer to the original object header, unchanged by views | 8 / 8 |
| `Weak<S>` | Nonnull pointer to its side table | 8 / 8 |
| Every object header | +0 nonnull ObjectDescriptor pointer; +8 u64 control (reserved zero for obj) | 16 / 8 |
| Side table | +0 u64 strong; +8 u64 weak; +16 immutable original object pointer | 24 / 8 |

The complete payload begins at header + 16 for every mode. Check 16 + payload size against 2^63 - 1 and allocation limits; allocation failure Aborts. Payload alignment above 16 is unsupported and diagnosed before generation. Do not add over-allocation, a private prefix or mode-dependent payload offsets. The handle's static Semantics, not the descriptor, selects count behavior.

Every count uses u64 with MaxRefCount = 2^63 - 1, including the internal weak guard. Increment at the maximum Aborts before update; never wrap or migrate just to extend the maximum. An even counted-header control stores strong << 1; an odd control stores sideTablePointer | 1. Clear only the low bit to recover the aligned address-space-0 pointer, preserving all high bits. Normal unpublished control starts at zero and publishes 2 for strong = 1 after construction. Final zero is never reused.

Side-table strong is 1..MaxRefCount while Alive and zero both while Building and after final release. **There is no Building sentinel.** Only a factory holding unpublished construction authority may change zero to one, exactly once; observing zero grants no such authority. Cyclic construction starts with strong = 0 and weak = 2: one guard plus the builder's Weak. Do not infer the complete lifecycle solely from count bits.

#### 21.2.3.2. Migration, promotion and release

Share the internal contracts ensureSideTable, retainStrong, tryRetainStrong, retainWeak, releaseStrong, releaseWeak and publishObject across all callers; these names are not public APIs. Each access requires valid ownership, borrowing or construction/destruction authority. Maintain exactly one authoritative strong-count location. rc is non-atomic; arc operations/transitions are linearizable. Published arc control/strong/weak accesses are atomic without mixing non-atomic accesses. Ordinary initialization is allowed only while storage is unpublished and inaccessible.

On the first downgrade, keep a valid strong reference and prepare an unpublished table with observed inline count n, weak = 1 and object = header. For arc, publish by CAS of the **whole control word**; only success transfers count authority to the table. On failure, retry the latest count or free the unpublished candidate and reuse another published table. Then retain a Weak and return it. Exactly one table is published; migration changes no strong count and never reverses. Cyclic objects have a table from the start. Inline increments/decrements also CAS the whole control, rechecking representation rather than overwriting a newly published table pointer with an old count. rc preserves the same transitions without atomics.

upgrade returns None for strong zero, Aborts at the maximum, and otherwise attempts a checked increment, retrying an arc CAS race. **Read table.object only after successful strong retention.** If final release wins, upgrade fails; if upgrade wins, its strong keeps the object alive. clone requires an existing strong and upgrade never resurrects zero. An expired table retains an immutable stale object pointer, which Weak cleanup must not dereference.

Final strong release changes one to zero, retains required descriptor/table information locally, destroys the complete dynamic payload using destroyValues when needed, frees the original allocation through freeStorage, then releases the weak guard. Never reload the header after free. Payload Weak cleanup cannot free the table prematurely because the guard survives object free. Final weak release frees only the table. Without a table, final strong release needs only payload destruction and object free. obj similarly destroys/frees without count decrement. Abort/nontermination prevents remaining cleanup/free; adjusted base addresses are never freed. Release without corresponding responsibility is an invariant violation.

#### 21.2.3.3. Runtime ordering

The following completion/effect ordering is normative for arc. An arrow requires program order or synchronization, including transitive effects; elapsed wall-clock order is insufficient:

```text
initialize side table -> publish control/Weak -> use table
initialize metadata/payload and finish builder cleanup
    -> publish completed object -> payload use through a runtime-acquired strong
all earlier strong releases -> final strong release -> complete payload destruction
free object -> release weak guard
all earlier weak releases -> final weak release -> free side table
```

The strong-release guarantee must survive inline-to-table migration. These obligations cover runtime initialization, counting, cleanup calls and frees; they do not define source-level concurrent payload access, thread transfer or a language memory model (Appendix D.2).

**Non-normative implementation candidate:**

| Operation | Candidate ordering |
| --- | --- |
| Resolve published table from control | Acquire |
| Publish migration CAS | AcqRel on success, carrying earlier releases |
| Strong/weak retain CAS | Relaxed |
| Successful upgrade CAS | Acquire; failure Relaxed |
| Strong/weak decrement | Release, with Acquire fence before final destruction/free |
| Unpublished initialization | Ordinary stores |
| Publish cyclic strong 0 -> 1 | Release |

On CAS failure, recheck the latest representation. A failure that discovers a table pointer still needs an Acquire observation before accessing the table. Inline decrement uses control CAS; table decrement may use fetch_sub. LLVM spells Relaxed as monotonic; compare-exchange orderings must meet its verifier constraints, including no release/acq_rel failure ordering. Alternative implementations must prove the normative arrows. Validate weak-memory behavior, not just possible interleavings, and verify generated IR/native count protocols separately. Allocation and destructors have no lock-free guarantee.

### 21.2.4. Value-borrow storage

ref/V and uniq/V point to the storage of the **immediate complete V**, without implicit dereference. On windows-x64-v1 each is one nonnull address-space-0 pointer, size/alignment/stride 8/8/8, aligned for V. It contains no metadata, length, count, Origin or Loan ID. A borrow of Array or Slice points to its complete handle. ref/(rc/T) points to a handle slot; objref/T points to the original object header. uniq/(rc/T) grants exclusive handle-slot access, not unique payload ownership. Nested dependencies stay distinct; ordinary @ref on an existing shared borrow copies it rather than adding a layer.

Zero-sized V keeps logical initialization, Loans and destruction. Use nonnull aligned substitute storage alive for the required uses; a static substitute per alignment may serve several distinct Places. Pointer equality does not merge those Places or their logical overlap. Size/stride remain zero, lifetimes do not extend, and substitute bytes justify no positive dereferenceable attribute. noalias follows the call contract of §21.5.5, not substitute addresses.

An unknown-layout generic V still uses one borrow pointer. Pass a separate GenericContext only if operations on V need it; transferring the borrow alone needs no V metadata. Direct value layouts must be finite. If materializing a required temporary needs unsupported storage, use a verified adapter/specialization or diagnose; do not silently box it or introduce an unsized value.

### 21.2.5. Concrete Closures and common function values

#### 21.2.5.1. Concrete environment

A concrete Closure environment E is a direct aggregate of its complete captured Types. Keep Binding Identity, logical capture order, mutability, Move Paths and Origin/Loan information separate from physical offsets. Apply §21.1.3's alignment-sorted aggregate layout. E has no embedded call pointer/header, user deinit, public fields or reflection members. Parameters, results and aggregate storage do not force it onto the heap. E is Copy exactly when all captured complete Types are Copy. Empty E and Function Items have size/alignment/stride 0/1/0; zero-sized captures keep their maximum alignment. Function Item identity, bound arguments and selected implementation are static information.

Calls use §7.6.3's Shared ref/E, Exclusive uniq/E or Consuming owner/E receiver. An owning Callable contract acquires E normally before adapting to a weaker implementation requirement. Shared/Exclusive calls do not own or destroy E and cannot Move owned captures. Consuming calls clean only the remaining initialized captures, in reverse logical acquisition order. Treat the implicit owned E binding as preceding explicit parameters: secure the result, clean body locals/defer, clean parameters, then clean remaining E last. Partial environments use CFG state and flags only where joins require them, not a mandatory per-Closure bitmap; no incomplete E can be acquired, erased or called. Zero-sized captures still run every required destructor.

#### 21.2.5.2. Common function storage and entries

A common Function Type F remains Owned-environment, Shared-callable and Non-Copy (§7.6.4). Its Windows handle has size/alignment/stride **16/8/16**:

| Offset | Field | Meaning |
| --- | --- | --- |
| 0 | environmentWord, 8 bytes | Inline E when size(E) <= 8 and alignment(E) <= 8; otherwise a nonnull pointer to E |
| 8 | operations: ptr | Nonnull immutable FunctionOperations pointer, aligned to 8 |

The environment's layout selects the mode; there is no tag. Unused inline bytes are unspecified, and the word need not be an integer or pointer. Even a zero-sized E with alignment 16 uses heap mode. Allocate heap E with Alloc(size(E)), using the allocator's zero-size backing and 16-byte alignment, without a header/prefix/alignment argument. Alignment above 16 is unsupported.

FunctionOperations is 24 bytes, aligned to 8: +0 nonnull callEntry, +8 nullable immutable context, +16 destroyEnvironment. Key it by E, F's contract, selected implementation, context schema/contents and FunctionAbi, not E alone. Its first two fields have the callee-record shape (§21.3.3), which alone proves no ABI compatibility. destroyEnvironment is null exactly when neither environment cleanup nor heap free is needed; a heap entry is nonnull even for cleanup-free E.

The logical entry arguments are below; their physical positions remain FunctionAbi choices:

```text
callEntry(environmentWord, arguments..., context) -> result
destroyEnvironment(environmentWord, context, location) -> ()
```

Both entries receive the **environmentWord value**, not the F slot address, through the matching FunctionAbi. Heap mode addresses E directly. Inline mode may spill the valid E bytes when an address is needed, preserving pointer provenance rather than prescribing an integer register. This ABI transfer is not a language Copy or capture reacquisition. An inline Shared-call spill is a temporary view, without destruction responsibility. The receiver Loan protects original F and its environment through the whole call. No borrow into hidden E may escape and no cross-call environment address is promised; pointers to external captures retain their original targets. Calls need no per-invocation allocation.

Destroying F consumes its responsibility and invokes its destruction entry at most once: destroy inline E only, or destroy heap E then free it. Null-entry destruction still consumes F logically. Moving F transfers its 16 bytes without a self-pointer into the inline word, capture reacquisition, allocation, count increments or required source clearing.

#### 21.2.5.3. Erasure, direct calls and optimization

Only a complete, Owned, Shared-compatible environment with a compatible signature may be erased. Results may borrow public arguments but not the hidden environment. Borrowed environments, Exclusive/Consuming common Types, common-value cloning and FFI callbacks are not introduced.

For conversion directly from Closure syntax, prepare uninitialized final F/environment storage before acquiring captures, then acquire them directly there in source order. Capture entries are binding Copy/Move/Borrow/Reborrow, not arbitrary user expressions; preserve their static effects and failure order. Required allocation failure reports the conversion site. For a general source expression, evaluate it once **before** any conversion allocation. An F-to-same-F acquisition is ordinary Move, not re-erasure. Combine completed E acquisition and final transfer only when §21.4.4's alias, lifetime, ordering and partial-state conditions hold; never overwrite live F early.

In §7.6.4's makeAdder example, the i32 capture occupies four inline bytes and the common function handle occupies 16 bytes. Repeated Shared calls retain that environment; moving the handle transfers its ownership.

Known Function Items/Closures call their selected entries directly; F uses callEntry. A shared generic Callable call uses the compile-time witness plan's selected entry/context pair without constructing F or a runtime witness table. Definitions, calls and adapters share one FunctionAbi mapping for logical receiver, arguments, result and context; matching ptr signatures alone are insufficient. Adapters never reevaluate source arguments, add a language Copy or reselect overloads/specializations. Shared destruction, free and adapter cleanup retain the original operation's source location; user deinit body failures retain their own locations.

Allocation count/location is not a language guarantee. Elision need not reproduce failure of a removed allocation/free, but must preserve acceptance, required checks, captures, user effects and destruction order; surviving resource operations keep their Abort and source-location contracts. Heap-to-stack environment promotion requires tracing all uses and cleanup and replacing entries with matching direct code. Never pass stack storage to a heap-free entry or add operation-table variants just for promotion; otherwise keep the heap representation. Physical ABI choices remain compiler-controlled under §21.4.2.

## 21.3. Generic code generation

This section integrates the adopted [Generic Sharing and Specialization design](../draft/Design/2026-09-13%20Generic%20Sharing%20and%20Specialization.md), which takes precedence over earlier specifications in its scope. The rules here and in §21.4.6 are normative; [Appendix B.7](appendices/B-reference-models.md#b7-generic-generation-and-specialization-strategy) records initial compiler guidance. Internal names describe plans, not new syntax, reflection, JIT, or a stable external ABI.

### 21.3.1. Policy and sharing conditions

**Instantiation** binds generic arguments. **Explicit full specialization** selects a user-written implementation under §8.8. **Automatic specialization** fixes facts in generated code while preserving that selected implementation. Neither instantiation nor a scalar operation alone requires a separate machine-code body.

Generation has these logical dependencies, without prescribing compiler passes:

1. Verify the generic body universally under its declared premises (§8.10), including operations, ownership, Loans and cleanup.
2. Check each use and select its implementation from the closed explicit-specialization set.
3. Substitute the verified plan and resolve concrete layout, acquisition, effects and cleanup.
4. Build a correct baseline sharing plan, then apply bounded optional optimization.

Sharing and budget changes preserve semantic acceptance, selected implementations, results, evaluation/effect order, checks, ownership and cleanup. Runtime metadata executes verified operations; it never performs lookup, proves Constraints or validates source Loans. Keep Origins in semantic proofs and dependencies even when generation keys erase them. Pattern coverage and required unreachable-code diagnostics are unchanged.

| Generation choice | Condition |
| --- | --- |
| Baseline sharing | Common representations, typed helpers, operations, storage and ABI adaptation preserve the verified plan |
| Required separation | Those mechanisms cannot preserve a semantic or representation difference |
| Optional specialization | Fixing facts improves the baseline without changing its meaning |

Scalar arithmetic and typed load/store may use small typed helpers while a larger body remains shared. Helpers retain the original checks, failure order and source location. No policy grants writes through ref, implicit Copy through uniq, or pointer comparison in place of a required referent operation. Diagnose unsupported paths before emission instead of generating a semantic substitute.

~~~kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func forward<T>(value: ref/T) -> i32 => classify<T>(value)
// forward<i32> calls the explicit implementation even with specialization budget 0.
~~~

### 21.3.2. Identity and generation keys

Keep semantic identity, implementation selection and generated representation distinct:

| Identity or plan | Required information |
| --- | --- |
| Complete static Type identity | Normalized nominal identity, every Semantics layer, nested Types and Origins; use alongside Loan information |
| Implementation Selection Key | Original function Identity and ordered ArgKey / LengthKey values for its own generic slots; select within the closed definition-side set |
| Runtime Object Type Identity | CoreId of the actual payload, using §21.2.1's root-handle rule |
| Generation plan | Selected implementation, typed operations and cleanup, entry/body ABI, reader schema and all embedded facts needed to preserve meaning |

Normalize and validate complete arguments before forming an Origin-erased key. Selection retains outer handle Semantics; it is not object identity. Origin differences alone neither select another implementation nor require machine-code duplication. Equal size or pointer representation proves no semantic equivalence.

A body may omit concrete Types handled wholly by context or helpers. Embedded signed arithmetic must still distinguish i32 from u32; direct destruction must retain the exact entry and embedded context. When destruction remains indirect, supply the actual operation/context pair. Equal size and a hasDestructor flag cannot justify erasing different destructors. Type identity, offsets, witnesses and other used facts each need a valid sharing plan.

[Appendix B.7.1](appendices/B-reference-models.md#b71-plan-keys-and-generation-order) separates semantic, body, entry and context keys for the initial compiler. These are in-memory generation roles, not a persistent object-cache format. Persistence follows §21.3.4.

### 21.3.3. Shared operations and Type policies

#### 21.3.3.1. Requirements and fixed facts

Shared, fully fixed and partly fixed bodies use the same lowering. Each typed operation obtains only its required facts from the verified substitution plan:

| Binding | Lowering |
| --- | --- |
| Fixed(value) | Use a constant, direct instruction or known entry |
| FromContext(slot) | Read the schema-defined slot Type |
| No requirement | Generate no slot |

Requirements need not correspond one-to-one with Type parameters. A body may need a size, Field offset, length or selected callee independently. Borrow transfer alone needs no referent metadata; value transfer needs its size and valid storage; destruction, Contract operations and calls use selected entry/context pairs. Typed helpers supply scalar operations and ABI adaptation.

Derive sizes, alignments, offsets, identity, acquisition conditions and operation pairs from the **same substitution**. Record every premise of embedded instructions and attributes as a fixed fact, and prove it for every connected substitution. Matching slot storage Types or sizes is insufficient.

~~~text
Fixed facts:
    size(T) = 16
    source and destination are valid, nonoverlapping 16-byte ranges
    both alignments are at least 8

Allowed: memcpy 16 bytes with align 8
Unproven: align 16, a derived Field offset, or a particular destructor
~~~

Use proven constant bounds for LLVM align and dereferenceable. Weaken or omit attributes when the common guarantee is insufficient; a runtime alignment slot is not a constant attribute. Stronger facts may apply within a proved branch, helper or specialization. Prove noalias under §21.5.5 and inbounds under its own pointer contract.

Retain width, signedness, floating-point rules, Semantics, effects and partial state in the typed plan. Keep obj/rc/arc/objref/objuniq acquisition and count operations distinct until equivalent low-level operations can safely merge. Unit retains zero-sized state and effects; Never has no value or normal return. Preserve raw-pointer pointee layout and Unsafe preconditions.

Metadata only runs acquisition plans already proved valid under §8.10. A conditional Copy/Move plan may use HasCopy, but runtime flags do not establish source legality. SharedReadResult (§4.6.6) remains a full-Semantics plan: owner Copy elements yield values, owner Non-Copy elements yield ref, object owners yield objref without retaining counts, and exclusive borrows yield shared Reborrows. Preserve correlated result Types, Origins and ABI mappings; slot borrowing is a different operation.

The Copy/Move and duplication examples in §8.10 also apply to shared lowering. In contrast, a pure borrow transfer can omit referent metadata entirely:

~~~kimi
func keepBorrow<T>(value: ref/T) -> ref/T from value => value
// No referent access, so no T metadata is required.
~~~

#### 21.3.3.2. GenericContext and operation pairs

GenericContext contains only the facts its reader needs. In windows-x64-v1 it is an immutable schema-defined sequence of 8-byte slots, at offset 8 * index:

| Logical slot kind | Contents |
| --- | --- |
| 64-bit integer | Nonnegative isize lengths, sizes, alignments and offsets; full-width u64 Type tokens; Boolean 0 or 1 |
| Entry pointer | Selected operation entry |
| Context pointer | That entry's private context, required metadata, or null |

The typed schema fixes each integer's signedness and purpose; LLVM storage is i64. Check layout/length values and slot arithmetic against profile limits at generation time. Never narrow a u64 Type token to nonnegative isize, convert pointers to integers, or add runtime slot tags.

An operation uses adjacent entry/context slots when both components are dynamic. Fixed components and unused requirements may be omitted. Contract witnesses and FrameLayout are compile-time correspondence plans, not runtime tables reached from GenericContext. Related Types, requirements and selected implementations remain in the semantic plan.

Ordinary shared bodies read size, offset and length directly, without traversing TypeContext. Facts used only by a callee stay in that callee's context. Merge identical semantic requirements; do not merge distinct requirements merely because their current values happen to match.

~~~text
Example context for a body using comparison, instantiated with T = i32:
    [0] isize   size(T)          = 4
    [1] entry   selected compare = compare_i32
    [2] context compare context = null
    [3] isize   offset(temp1)    = 4

If temp1's offset is fixed for every member of the body, omit slot 3.
Invoke slots 1 and 2 using the comparison entry's ABI.
~~~

A direct integer needs one slot read; a fully dynamic operation pair needs at most two. This describes the access path, not final instruction counts or a speed guarantee; the operation may read its own metadata, and flattened contexts may use more bytes.

#### 21.3.3.3. Type metadata, schemas and cycles

ValueMetadata retains §21.2.2's 48-byte format. TypeContext serves the **Type operation entry**: it may hold Field offsets, element/Case metadata and selected operation pairs. Fixed-array TypeContext includes elementMetadata and N. Compute the entire derived TypeLayout from the closed substitution; derive body integer slots from that same plan instead of rebuilding layout at runtime. Handle metadata cannot substitute for object payload metadata.

A schema describes reader requirements, definition-side bindings, normalized Type/length expressions, operations and slot Types. Deduplicate and order by stable structural requirement keys, keeping each dynamic operation pair adjacent. Source occurrence, registration order and parallel completion do not determine the schema. The callee's internal schema is not part of the caller's schema.

Generate and validate each entry together with its own context. The caller passes that context opaquely and never interprets its schema. Reader changes may remove unused slots without schema-only adapters. ABI differences still require §21.3.6's adaptation.

Metadata and contexts are immutable constants for closed substitutions and may form finite cycles, including mutual recursion. Do not require a DAG. After optional choices, resolve final references and validate all pairs and fixed facts before executable output; do not publish incomplete records. A changed callee context need not change the caller ABI or optimization choice.

Keep records and entries alive through every use and cleanup, independently of caller stack storage. No per-call context construction or runtime Type search is required. Store no instance initialization state, Loans or dynamic collection lengths in shared metadata; null context denotes no required context.

#### 21.3.3.4. Lengths and fixed-array destruction

Length meaning and formation proofs remain owned by §4.4:

| Use | Source |
| --- | --- |
| Explicit-specialization selection | Static LengthKey |
| Body N, bounds, Slice length and element loops | Integer slot or Fixed |
| Array transfer size | Integer slot or Fixed from the same TypeLayout |
| Fixed-array destruction | elementMetadata and N in the array's TypeContext |
| Length needed only by a callee | That callee's private context |

Identify length requirements by normalized expressions and definition-side bindings. Prove Type formation from declared premises and check concrete evaluation; body-only Type formation cannot wait for favorable arguments. Do not infer N from size/stride or make a body read N through TypeContext. Keeping N both in body slots and destruction metadata is permitted when both readers need it.

~~~kimi
func keepArray<length N, T>(value: [N of T]) -> [N of T] => value

func lengthAfterOffset<length N>(value: ref/[(N + 4) of u8]) -> isize
    return N + 4

func twiceLength<length N>() -> isize => N * 2
// keepArray transfer needs size only.
// The signature's ValidLength proof can eliminate the N + 4 calculation.
// N * 2 is ordinary checked arithmetic: overflow remains runtime Abort.
~~~

Authorized transfers follow §21.4.5; fixing a length alone does not remove unrelated arithmetic checks. For example, specializing this loop for a small N may remove proven bounds checks or unroll it, but must retain any unproved total overflow check:

~~~kimi
func sum<length N>(values: ref/[N of i32]) -> i64
    var total: i64 = 0
    for i in values.indices
        total += values[i]@i64
    return total
~~~

For byte-offset multiplication attributes such as nuw, prove that 0 <= i < N for the same array dominates the multiplication, stride is nonnegative, N * stride fits the operation width and layout limit, and operands are defined. Pointer provenance, addition and inbounds need separate proof. Zero stride does not remove logical bounds checks.

Destruction uses the pair {destroyValues, metadata}; metadata is the existing entry's context argument in destroyValues(first, count, metadata, location), not an extra argument. Its operation ABI need not order arguments like an ordinary call. Apply §21.2.2's null-entry, zero-count, complete-range, reverse-order and source-location rules.

A fixed-array entry reads elementMetadata and N from its own metadata's TypeContext. It destroys the outer count arrays in reverse order and each array's N elements in reverse order, skipping calls for null element entries or N = 0. Storage release is separate.

~~~text
arrayDestroy(first, count, arrayMetadata, location):
    elementMetadata, N = arrayMetadata.TypeContext
    for each array in reverse logical order:
        if N != 0 and elementMetadata.destroyValues != null:
            elementMetadata.destroyValues(arrayFirst, N, elementMetadata, location)
~~~

A complete contiguous range may become one count * N element-destruction call only when metadata, range, reverse order and failure location agree. If that product exceeds the count width, retain the original loops without adding an Abort. Preserve all logical destruction effects even at zero stride.

### 21.3.4. Artifacts, verification, and invalidation

#### 21.3.4.1. Closed selection and dependency validation

The defining Kotonoha closes its explicit-specialization set under §8.8.3. Artifacts retain declarations, selection sets/mappings, complete contracts, verified bodies and legitimate deferred obligations. Preserve defining Symbols, environments and dependencies without exposing private names to callers.

Check every environment-selected specialization's target, arguments, Constraints, inherited contract and body before finalizing its artifact, even if unused. Excluded syntax follows §19.5. Unused verified bodies need no machine code; every use still checks its own contract, initialization and Loans. Shared calls and function references reach the statically selected implementation, never one selected from a value's Dynamic Type or registration/load order.

Preserve and validate the complete declaration, body, selection, premise, and absence dependencies under [§18.7](18-modules-and-dependencies.md#187-verified-information-and-reuse). Revalidate changed selections before generation; never mix old proofs and new mappings or choose an incorrect shared fallback.

#### 21.3.4.2. Persistence and composition

Persistent semantic plans, invalidation, and observable source information follow §18.7. Regenerate ABI plans, schemas, contexts, frames, budget choices, IR, and machine code; native input summaries are separate. Persistent product generation choices remain deferred until measured search cost and a complete validation contract justify them. Such a contract must cover baseline plans, candidate sets/order, estimates, budgets, compiler/profile, selected implementations, and generation dependencies; a small saved choice is not proof of validity. Object-code caches and persistent runtime context graphs are not introduced.

Record operations, selected implementations, schemas and constants actually used by generated artifacts. Changes to these dependencies must revalidate/regenerate affected witnesses, inlined bodies and entry/context pairs; conservative invalidation is allowed until precise dependencies exist. Source distribution and deferred binary interfaces follow §18.3–18.7. These rules do not define CompositionId or Entry/Provider selection; future Composition Root connections require a separate adopted contract (§13.8).

### 21.3.5. Generation limits and code merging

Separate **mandatory generation resource limits** from **optional optimization growth budgets**. Identical inputs, compiler/profile and settings reproduce logical plans and budget allocation independently of enumeration, parallel completion and cache presence. This does not promise identical behavior under actual OS resource exhaustion.

Required closed substitutions, layouts, metadata, contexts and plans must fit finite generation limits. Distinguish finite graph cycles, growth of distinct keys such as `T -> Box<T>`, and invalid infinite inline layout. Body sharing alone does not bound metadata generation. Reuse existing logical plans for already registered keys; do not enumerate the full Cartesian product of arguments or use fake pointers for missing facts.

Failed or exhausted optional exploration keeps the verified baseline and cannot by itself reject source. If required baseline generation also exceeds limits, issue a resource diagnostic, separately from semantic or representation errors. Retain required checks and unsupported-feature diagnostics even for definitions with no emitted machine code. Budgets never replace an explicit specialization with the ordinary body.

Equivalent code and entries may merge only while preserving identity, results, effects, failures, ownership and cleanup. Emit needed immutable metadata/operation/context records as private unnamed_addr constants when their addresses are unobservable. Constant folding and direct calls retain the typed plan and consumer contract. Reuse TypeLayout, ValueLowering, FunctionAbi and CleanupPlan rather than cloning syntax.

[Appendix B.7](appendices/B-reference-models.md#b7-generic-generation-and-specialization-strategy) gives the initial bounded selection and accounting method. Budget numbers and estimates are not fixed language constants or exact limits on final binary size or compilation time. Local specialization hints, dynamic storage for unknown substitutions and object caches remain deferred under Appendix D; no new require/forbid-specialization syntax is added.

### 21.3.6. Entry ABI and call responsibility

#### 21.3.6.1. Budget-independent entries

Choose the caller-facing entry ABI from the call contract:

| Call | Entry ABI |
| --- | --- |
| Fixed callee and known substituted signature representations | The same FunctionAbi rules as a nongeneric function |
| Context callee pair, or unresolved signature representation | Shared ABI based on the generic declaration's signature |
| Common function value | Existing Function Type contract (§21.2.5) |

The shared ABI passes representation-dependent acquired values by storage pointer and results by pointer to caller-provided uninitialized storage. Use a declaration-known common representation directly; ref/uniq keep their existing one-pointer form. A Fixed entry alone does not make an unresolved signature scalar.

Optional budgets change entry implementations and internal adaptation, **not the entry ABI used by callers**. Generate needed entries per call contract and connect them to shared or specialized bodies. Merge entries only when their ABI and complete implementation facts agree.

~~~text
concrete caller -> direct_i32_entry(i32, i32) -> i32
                       -> adapt to storage -> shared body
                       or -> specialized body

shared caller -> {shared_entry, private context}
                       -> shared body or adapted specialized body
~~~

When ordinary FunctionAbi uses scalar passing, the concrete caller keeps it even at budget zero; extra transfers/calls inside the entry may remain. Reverting an optional candidate reconnects its entries to baseline bodies without specializing or rolling back a recursive caller group. Generic scratch frames follow §21.4.6.

#### 21.3.6.2. Connection validation and adapters

The logical call unit is {entry, context}. Pair generation and opaque context handling follow §21.3.3.3. At every connection, statically match the **generation scheme identity** and entry ABI. The scheme covers compiler build, target/profile, ABI/metadata/context generation rules and settings that change those rules.

Entry ABI records logical arguments/results, ownership and context plus their physical Types, positions, calling convention and attributes. Indirect calls need this typed contract, not runtime ABI tags per slot. Callee schema changes are internal to the entry/context pair; no adapter exists solely to translate schemas.

Within one scheme, adapt real ABI differences such as scalar results versus result storage. Regenerate or reject artifacts from another scheme rather than adding scheme adapters or runtime checks. OS startup, FFI and external backend helpers retain their external contracts. Later argument elimination or calling-convention changes require proof of compatibility across all uses; symbols, unused-entry emission and physical argument positions are not public guarantees.

#### 21.3.6.3. Acquisition and cleanup

Apply §21.4.3's responsibility transitions at the **logical callee entry**, regardless of entry/adapter/body splitting. Evaluate and acquire receiver/arguments once in language order; adaptation adds no language Copy/Move or duplicate cleanup. Owned argument pointers, ref, uniq and result storage keep distinct contracts despite identical pointer representation.

The same normal-return and caller-storage rules apply to generic scratch reserved by an entry (§21.4.6).

### 21.3.7. Product and test generation

For code generation, first fix product substitutions, sharing classes, call entries, frames, and budget choices from product inputs and normal-output roots. Semantic-only test checking and `--list` need not construct this plan. Source membership and dependency partitions follow §18.8.

Additional test requests, including new substitutions of product generics, belong to the test region. Reuse existing product entries for identical substitutions. A new test entry may connect to an existing product body only after validating the existing contract; do not enlarge product sharing classes, context schemas, or frames. Put required additions in the test region.

Account for product and tests separately, with all test cases sharing one test budget. Never transfer candidates, savings, or unused budgets between regions. Reused product code does not count again in test B/Bf. External source bodies count in the region requesting generation; separately linked existing native code does not. Break ties using product declarations/semantic information, not SourceIds or whole-input hashes that include test source.

```text
fixed product plan and budget
  -> tests reuse existing product entries
  -> test-only substitutions, bodies, and budget
  -> emit the required test generation closure
```

After fixing the plans, emit only the closure needed from all test execution roots, including selected implementations, initialization/destruction, cleanup, runtime helpers, entries/contexts, and metadata. Omitting product code does not redistribute product budgets or omit required verification. Case filters do not change the compiled all-case artifact under §20.9 and §22.6.

Fix test artifact inputs/settings under §18.8. Reuse product plans only under the validity conditions above; changed dependencies/settings require affected product plans to be revalidated/fixed before adding test requests. This need not perform two full compilations or emit two files: one final LLVM module may hold both regions. Different execution roots/reachability and later optimization need not produce byte-identical whole binaries. Entry/Provider composition remains deferred rather than an implicit test-build input.

## 21.4. Checked lowering and internal ABI

### 21.4.1. Input and generation set

Lowering consumes finalized, read-only semantic information: complete Types, selected implementations/callees, acquisition and evaluation order, CFG edges, initialization/consumption and destruction responsibility, validated Loans/Origins/lifetimes, cleanup plans, source locations, and supported layout/ABI/runtime operations. It does not re-resolve names or reconsider ownership legality. A separate target-independent Lowered IR is optional; direct LLVM generation from verified CFG and semantic information is allowed.

Share stable Identities without cloning the syntax tree. Compute and reuse these distinct records:

| Record | Information |
| --- | --- |
| TypeLayout | Mode, size/alignment/stride, LLVM storage Type, Field/base Identity to offset mapping |
| ValueLowering | Computation/storage representations, conversions, valid-bit constraints |
| FunctionAbi | Physical signature, argument/result slots, calling convention, ABI and justified optimization attributes |
| CleanupPlan | Edge-specific initialized/moved parts, ownership, registration, logical destruction order |

Unresolved Types/obligations, missing cleanup, or unsupported selected operations fail generation. Zero, undef, poison, unreachable, and freeze are not substitutes for unresolved language semantics.

Plan selected product implementations, startup, required concrete generic implementations, dependencies, Core, cleanup, and runtime helpers under normal-output roots. Foreign imports have no emitted body. Use a worklist keyed by declaration Identity, concrete arguments, and selected implementation. Ordinary recursion reuses a declaration; unbounded distinct instantiations receive a resource diagnostic. After required verification and planning, omit unneeded machine code/metadata; test emission follows §21.3.7 without reallocating product budgets.

Unused nongeneric bodies and unexecuted branches within generated bodies still receive required semantic and unsupported-feature diagnostics; code omission cannot hide them. Excluded syntax is outside this set. Verification of uninstantiated generic bodies is unchanged. Unsupported sharing/metadata must not silently select another implementation. Code may be omitted or removed only after required checks.

### 21.4.2. Physical function signatures

The internal function ABI is compiler-controlled within a final generation and deliberately unfixed. Source dependencies may participate in that common generation; no external ABI is needed between their separately verified semantic plans. The ABI applies to user functions, Core, and private runtime helpers in Application and Library inspection output. No physical calling convention, parameter/result representation or ordering, hidden-context position, symbol spelling, or stable ABI version is a language guarantee. The compiler may choose different physical signatures across builds, targets and generated functions without a language-version change, subject to language and external contracts. Within a scheme, generic callers use §21.3.6's entry contracts: optional budgets change implementations, not caller-facing entry ABI. Independently generated modules or compiler builds have no promised binary compatibility.

Within the entry rules of §21.3.6, direct or indirect passing, aggregate splitting/coercion, result storage, omitted slots, calling conventions such as LLVM ccc or fastcc, and ABI attributes such as byval or sret are compiler choices. Storage layouts defined elsewhere do not fix function passing: this includes scalars, string, object handles, and Core.Weak<S>. Unit still has its logical value/effects, and Never still has no normal result or return edge. Every emitted representation requires an implemented ValueLowering and its complete validity, ownership, and cleanup operations; implementation freedom does not permit guessing an unsupported representation.

Derive definitions and all calls, including indirect entries and adapters when supported, from the same FunctionAbi contract. Physical rearrangement must preserve the mapping to logical parameters and results. Evaluate and acquire explicit arguments once in source order. A value receiver is evaluated once before explicit arguments (§7.3); a Type-qualified unbound call supplies self in ordinary argument order. Physical slot order does not determine evaluation order. Select ABI attributes according to the actual backend contract and prove any additional validity or optimization premises; a calling convention or attribute cannot grant source-level Copy, Move, or alias permissions.

An ABI change must update every affected definition, caller, adapter, and compiler-generated runtime helper consistently, and invalidate incompatible generated/cache artifacts. Use §21.3.6's generation scheme identity and typed entry contracts; no separately published internal ABI version or compatibility window is required. This initial design rebuilds generation artifacts rather than persisting them (§21.3.4). The specified OS entry, foreign C calls, backend-support symbols, and externally observable storage retain their own contracts. In particular, backendSupport.abiVersion identifies the external backend supply, not a stable Kimigayo function ABI.

**Non-normative example.** One compiler implementation can use direct scalar passing and caller-provided aggregate slots as follows. These signatures illustrate a possible lowering, not a required or frozen ABI.

```kimi
group Samples
    func isPositive(value: i32) -> bool => value > 0
    func echo(text: string) -> string => text
    func echoPair(value: (string, i32)) -> (string, i32) => value
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

The following responsibilities are language semantics, independent of the physical ABI. Argument temporaries and secured results are logical values; the compiler need not allocate a separate physical slot for each. The table also describes a caller-provided result-slot implementation when one is used.

Acquire arguments once in source order. Copy preserves its source; Move transfers responsibility into the argument temporary. Allocating a slot is not Copy. A Copy source cannot share mutable storage with the callee's acquired value when that would expose consumption or modification of the source.

| Point | Acquired argument values | Secured result / optional result storage |
| --- | --- | --- |
| Acquisition / just before call | Acquired values are caller responsibility | No result yet; optional storage is uninitialized |
| Callee entry | Responsibility transfers to callee | No result yet; optional storage is uninitialized |
| Result secured, cleanup running | Remaining parts are callee responsibility | Secured, still callee responsibility |
| Normal return edge | Consumed or cleaned; caller must not destroy again | Responsibility transfers to caller; caller's Initialized fact begins here |
| Abort or nontermination | No later normal cleanup/return | Caller neither reads nor destroys it |

Transfers during argument acquisition use the caller's cleanup plan for already acquired temporaries. A callee that was never entered cannot perform their cleanup. The callee never frees caller-owned stack storage. Securing a result before cleanup does not make it available to the caller: cleanup must finish before return. If it Aborts or diverges, later cleanup and result delivery do not occur.

Omitting physical storage for a zero-sized argument or result preserves evaluation, acquisition, parameter responsibility, and initialization on normal return. If a zero-sized value needs an address, the implementation must satisfy its alignment and lifetime without changing its language size or stride. One possible implementation uses a one-byte substitute slot, such as `alloca i8, align 8`. Shared substitute slots meet maximum alignment and remain alive through the last use; Place Identities retain separate state and responsibility. Their existence grants no positive dereferenceable guarantee for the semantic zero-byte value.

### 21.4.4. Values, Places, and control flow

Keep acquisition, Place evaluation, first placement, and replacement distinct. First placement writes uninitialized storage without destroying an old value. Replacement secures the right side, evaluates the left Place, destroys its remaining old parts, then places the new value. A setter receives the secured value instead of an automatic old-value destruction/store; simple assignment does not call the final target's getter. Compound assignment remains target-first (§13.7).

Standard stored access uses TypeLayout; custom/computed/required access retains the selected callable contract, including restrictions after witness optimization. Self-assignment cannot be removed based only on address equality.

```kimi
values[index()] = makeValue()
// makeValue -> index -> old-value cleanup -> placement
let ok = divisor != 0 and (100 / divisor > 1)
// Division and its checks run only on the true edge of divisor != 0.
```

Use CFG edges for short circuit, branches, match guards, and loops. Evaluate conditions/subjects once. Scalar joins use phi from actual normal predecessor blocks; aggregate joins use a common initially uninitialized result slot, secured on every normally arriving path. A path may secure its result directly in that slot before its required cleanup; this does not deliver the result to the enclosing expression until cleanup completes and the path arrives. If cleanup Aborts or diverges, the enclosing expression neither reads nor destroys the secured result. This permits avoiding an additional transfer on the arrival edge while preserving §16.2's acquisition, cleanup and delivery order. Unit needs no phi, and Never supplies no fictional value/edge. phi predecessors are the blocks after cleanup. select may replace conditional evaluation only when evaluating both alternatives early is proven legal.

A non-Never Expression Type does not guarantee a normal CFG predecessor: required cleanup may prevent delivery (§14.9). Do not manufacture an incoming value or change the checked Type to Never in that case.

Keep match verification reachability distinct from ordered runtime dispatch. Arms excluded by earlier Patterns still receive required checking, but contribute no runtime predecessor, result arrival or lifetime update. A separate dispatch plan must preserve the verified state at every selected arm. Unguarded Pattern tests have no acquisition or cleanup effects; guards require their own state transitions. When prior tests and exhaustiveness guarantee the next arm succeeds, dispatch may enter it directly without inventing an unmatched value or failure path.

Verification must carry earlier false-guard effects, after guard cleanup, into later arm checking. A source-ordered verification chain may conservatively retain mismatch edges even for irrefutable or covered Patterns, joining those states with guard-false states at the next test. It must not add an unmatched normal completion. Runtime pruning removes only proved-impossible paths; it must not reset earlier guard effects or initialize body bindings before successful guard cleanup.

Direct construction into an uninitialized final slot may remove intermediate transfers only if evaluation order, aliasing, Loans, storage identity/lifetime, intermediate observations, partial initialization, and cleanup remain unchanged. Otherwise keep an independent temporary; never overwrite a live replacement target early.

```kimi
var useOriginal = true
var pair = ("old", 1)
pair = if useOriginal => pair else => ("new", 2)

let saved = work: do
    defer => pair = ("later", 3)
    exit to work: pair
// saved receives the pair secured before defer. pair holds ("later", 3).
```

The result slot may contain secured bytes while cleanup runs, but consumers of the enclosing expression must wait for normal arrival. Reusing a slot for another evaluation starts a new logical lifetime; a previous evaluation's arrival does not authorize consumption in the new lifetime.

### 21.4.5. Cleanup and physical transfer

Tuple and fixed-array construction may place components directly in their final subslots. Each completed placement retains its own cleanup responsibility until whole-value completion transfers those responsibilities to the aggregate; completion need not copy bytes again. Physical offsets follow TypeLayout, while acquisition and cleanup keep their logical order. This optimization does not publish a partially constructed whole value.

Each scope-leaving edge secures its result, performs exactly the cleanup of scopes left, then delivers the result/transfer. Follow §16.2's inner-to-outer, reverse lexical order of locals and defer; process only registered defer and initialized parts still owned. Use edge-known state directly; introduce runtime flags only where paths must remain distinguishable after a join. Equal cleanup sequences may share code when state and destination match. A heap stack or closure per defer is not required. Never infer partial construction/Move/base state from value bits or addresses, reorder cleanup by physical offsets, or move destruction to last use.

Abort stops cleanup; nontermination blocks subsequent cleanup and delivery. Lack of visible side effects does not license removing a language-permitted infinite loop.

Every currently storable complete value supports an authorized Copy or Move by transferring its size bytes, without user calls, allocation, count increments or pointer fixups. Copy preserves source responsibility; Move transfers it. For nonzero size, source and uninitialized destination ranges must not overlap. Moving an object handle leaves the object body and pointers to it in place. A future address-dependent value representation must revise this contract and metadata schema rather than silently requiring relocation callbacks. Padding and unused environment bytes are transferable but not semantic values or automatically defined ABI-coercion bits.

For a contiguous range relocation, checked count * stride bytes may use memmove-equivalent transfer under exclusive move authority. Dispose of overwritten responsibility before relocation, preserve exactly one responsibility per logical element under overlap, and expose no intermediate partially moved state. Zero-sized elements retain logical transfer and cleanup effects.

Prefer direct Field/scalar operations and avoid slots created only to use a memory intrinsic. For valid transfers, prefer nonvolatile llvm.memcpy on nonoverlapping ranges, llvm.memmove on authorized overlapping ranges, and llvm.memset for required filling. Preserve alignment and constant lengths. These operations implement already-authorized acquisition; they grant no Copy/Move or whole-value read of partial data. Zero-byte instructions may disappear while state updates and destruction remain. LLVM chooses expansion/vectorization/libcalls; memcpy.inline is reserved for constant-length sites requiring no libcall. Metadata range destruction follows §21.2.2; destroying a value and freeing its enclosing storage are separate operations, with free occurring only after normal destruction.

```llvm
declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1 immarg)
; Inside a function: valid, nonoverlapping 24-byte ranges.
call void @llvm.memcpy.p0.p0.i64(ptr align 8 %dst, ptr align 8 %src, i64 24, i1 false)
```

### 21.4.6. Generic scratch storage and fixed frames

#### 21.4.6.1. Reuse and lifetime

Prefer direct result construction, reuse of acquired owned-argument storage and removal of intermediate transfers under §21.4.4's evaluation, alias, Loan, storage-identity and cleanup conditions. Keep a Copy source separate from the callee's acquired value; never write a live replacement target early.

After an owned argument is Moved, its storage may be reused only when no old responsibility or reference remains and capacity/alignment fit. This does not permit assignment to an immutable source parameter. Values with overlapping lifetimes, including cleanup and final references, need distinct regions. Loop temporaries may reuse a region across nonoverlapping iterations without moving destruction to the last read.

#### 21.4.6.2. Exact entry reservations

A temporary whose size and alignment are fixed in a body may use that body's static frame. Lay out remaining storage for each closed substitution. Its entry reserves the **exact required capacity**, rounded to the maximum required alignment, using fixed-size entry alloca. Do not use another instance's maximum or a capacity bucket.

Pass per-call scratch to the shared body as a separate internal pointer argument. Supply offsets as Fixed facts or integer context slots. Include storage required for ABI adaptation, such as materialized scalar arguments and result slots, in the plan.

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

Statically validate every offset, capacity and alignment against the entry's reservation. The shared body performs no runtime scratch layout or dynamic scratch allocation; ordinary source-required heap allocation keeps its existing contract. Loops do not accumulate per-iteration stack allocations, and recursive invocations have independent live storage.

Share entries only when entry ABI, capacity/alignment, destination body, adaptation and all embedded constants/context references agree. Capacity zero removes the allocation, but ABI/context differences may still require an adapter.

Never store scratch in shared context or let a borrow into it escape in a result or Closure. Moving its values out, including values containing legal references to external storage, remains allowed. Normal return releases the frame without duplicate destruction; prohibit a tail call that discards the frame while scratch is still used.

#### 21.4.6.3. Initial profile boundary

The initial fixed-stack path supports alignment at most 16. Do not reduce a Type's alignment; diagnose unsupported representation when no valid path exists. Generate required Windows stack probes and unwind information for fixed frames.

Zero-sized values use §21.2.4's aligned substitute storage while retaining state, Loans and destruction counts. Substitute addresses prove neither positive dereferenceable ranges nor noalias. Exact capacity describes the planned reservation, not the final machine frame including spills and call areas.

Dynamic alloca, heap fallback for scratch and new stack-exhaustion/allocation-failure contracts are not introduced. Preserve the existing Library output boundary; unknown substitutions are outside this generation scope.

## 21.5. LLVM Windows x64 profile

### 21.5.1. Target and optimization

The initial profile is **windows-x64-v1**: LLVM **22.1.8**, target **x86_64-pc-windows-msvc**, CPU **x86-64**, features **+sse2**, relocation model **pic**, code model **small**, asynchronous unwind tables. Use the target's verified DataLayout (§21.1). Do not infer extra features from the build machine, require AVX, or compensate for absolute 32-bit data references with /FIXED or a low image base. Diagnose static artifacts outside the code-model range separately from heap size limits.

Every generated function definition, including entry and runtime, carries matching target-cpu, target-features, denormal-fp-math=ieee,ieee, and uwtable(async) (LLVM uwtable is equivalent). Emit:

```llvm
target triple = "x86_64-pc-windows-msvc"
; Also emit the verified target datalayout.
!llvm.module.flags = !{!0}
!0 = !{i32 8, !"PIC Level", i32 2}
```

The compiler emits pre-optimization IR. O0 skips the general IR optimization pipeline and uses llc -O0; default O2 uses opt default<O2> followed by llc -O2. opt reads CPU/features from function attributes; do not duplicate them as opt -mcpu/-mattr options. Match llc and manifest settings. Verify before and after optimization.

Use LLVM for inlining, constant propagation, dead-code elimination, SROA, mem2reg, instruction selection, and register allocation. No default O3, unconditional alwaysinline, loop unrolling, or redundant general SSA optimizer is required. Internal ABI changes by whole-module optimization are allowed only when all uses and semantics remain consistent; preserve external ABI and observable storage. O0/O2 cannot change acceptance, checks, cleanup, or nontermination.

### 21.5.2. Module, symbols, and caches

One project/target emits one .ll with target information, private constants, required external declarations, internal runtime/Core/user/cleanup definitions, and Application entry (§22.2). Source public visibility is not native export.

| Symbol | Linkage |
| --- | --- |
| User functions, public main, implicit body, Core, cleanup, runtime helpers | internal definitions in both output kinds |
| Application __kimi_start | external definition; absent in Library |
| Windows APIs / LibraryImport | external declarations; dllimport follows library kind (§20.8.2) |
| Backend marker _fltused | Strong external data definition (§21.5.7) |
| Internal constants | private; literal sharing follows §21.5.6 |

Emit one definition for each generated function, without a same-name declare. Explanatory signature-only fragments are not complete modules. Mangle source names with Kotonoha/declaration Identity, arguments, and implementation selection.

Use one final-module symbol table. Same-named external declarations share only if physical Type, calling convention, ABI attributes, dllimport, and actual provider all agree under §20.8.2, regardless of consumer aliases. Function/data and declaration/generated-definition collisions are errors. Reserve __kimi_ for compiler internals, llvm. for LLVM, and _fltused, __chkstk, memcmp, memcpy, memmove, memset for profile supplies; reject these external names in user LibraryImport. Match exact ABI symbol names. Quote/escape LLVM identifiers and UTF-8 bytes; never insert raw source strings into IR. Shared ptr signatures do not merge source unsafe contracts; attach only guarantees true for every use.

Deterministic generation records retain compiler/layout and generation-scheme identities, entry/body ABI contracts, target/DataLayout/codegen settings, backend package version/hash, selected fragments/generated sources, complete arguments and selected implementations, cleanup, callees and helper dependencies. Validate content identities without requiring a public internal ABI version. Under §21.3.4, persistent semantic plans validate their own dependencies; generation records and code are rebuilt under the current scheme, not restored from an object cache. Size/alignment alone is never a sufficient key. Do not depend on absolute working directories, host locale, enumeration order, or host CPU.

### 21.5.3. Checked instructions and raw pointers

Preserve existing arithmetic, conversion, indexing, and failure order. nsw/nuw, poison, or undefined behavior cannot implement a required Abort check.

| Operation | Initial lowering |
| --- | --- |
| Integer add/subtract/multiply | Signed/unsigned overflow intrinsic or equivalent result-plus-overflow check; Abort on failure |
| Negation, increment/decrement, compound assignment | Check first; commit the write only on success |
| Integer division/remainder | Check zero divisor and signed minimum / -1 before the instruction |
| Shift | Check count in its original Type: 0 <= count < left width, then convert; ashr for signed right shift, lshr for unsigned, shl without treating discarded bits as arithmetic overflow |
| Integer conversion | Check destination range before extension/truncation |
| Float to integer | Ordered range checks below, then fptosi/fptoui only on success |
| Integer to float / float width conversion | Required rounding; detect finite-to-infinity failure |
| Array/index/Range | Required bounds checks before successful address calculation |

Constant evaluation follows §17.3.4. Prove success before removing a check; folding a failing path to Abort or removing an unreachable path is allowed.

```llvm
declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
; Inside a function:
%pair = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %a, i32 %b)
%sum = extractvalue { i32, i1 } %pair, 0
%overflow = extractvalue { i32, i1 } %pair, 1
br i1 %overflow, label %abort_overflow, label %success
; abort_overflow calls the generated noreturn Abort helper with the source site.
```

**Float-to-integer bounds.** For source precision p (24 for f32, 53 for f64) and destination width N = 8,16,32,64 (64 for isize/usize), test x directly:

| Destination | Lower bound | Upper bound |
| --- | --- | --- |
| iN, N <= p | x > -2^(N-1) - 1 | x < 2^(N-1) |
| iN, N > p | x >= -2^(N-1) | x < 2^(N-1) |
| uN | x > -1 | x < 2^N |

All constants are exact in the source format. Ordered comparisons reject NaN/infinities. Only then execute fptosi/fptoui, which truncates toward zero. No trunc/truncf helper or llvm.trunc is needed; do not speculatively convert and hide poison with select. Thus -128.75 to i8 yields -128, -0.75 to u8 yields 0, while -1 to u8 and f32 2147483648 to i32 Abort.

**128-bit subset.** Support storage/acquisition, internal arguments/results, comparisons, bit operations, shifts, integer conversions, and checked add/subtract/negate/increment/decrement/multiply, subject to verification that LLVM 22.1.8 needs no unsupplied helper. Diagnose i128/u128 division/remainder and both directions of f32/f64 conversion, including compound assignments, before optimization even in unused bodies/branches. Required constant evaluation and fitted constant storage are separate. __divti3, __udivti3, __modti3, __umodti3 and __fix*ti/__float*ti* helpers are not supplied initially. Verify actual multiplication expansion rather than assuming a helper from its name.

**Raw pointer operations.** Use the same address-space-0 ptr for allowed pointer-Type casts, icmp eq/ne for same-Type equality/null tests, ptrtoint to i64 and inttoptr from i64 for usize conversions. Use a storage-Type GEP with i64 index for p + n and sub i64 0, n for p - n; no inbounds or nsw/nuw in the initial form. Check GEP spacing against positive stride(T). Zero displacement preserves null as well as other pointers. Arithmetic alone proves neither alignment nor initialization. These operations retain §5's unsafe allocation/provenance, mathematical displacement, and no-wrap conditions; violations need not Abort. Integer reconstruction creates no extra dereference permission. Typed access still needs valid range, alignment, permissions, initialization, and replacement legality. Runtime buffer arithmetic has its own checked contract (§22.5.3).

### 21.5.4. Floating-point environment

Use ordinary fadd/fsub/fmul/fdiv/fneg/fcmp and conversions, without fast-math, reassociation, approximation, or FP fusion. Preserve §13's NaN, infinity, signed-zero, subnormal, rounding, and Equatable distinctions. denormal-fp-math is ieee,ieee, including any f32 override.

Require Windows x64 ABI-standard MXCSR control state: nearest/ties-even, DAZ off, FTZ off, hardware FP exceptions masked. This is the startup and foreign-code connection contract, also assumed by inspection-only Library IR. External code that temporarily changes control bits restores them before normal return; deliberate environment-changing APIs are unsupported. External initialization follows the same condition. No startup MXCSR setter, per-call save/restore adapter, individual preservation flag, constrained intrinsic, strictfp, or FP-environment noinline is generated.

MXCSR status bits are volatile and not exposed. External code must not depend on status flags left by Kimigayo calculations. Violating these boundary conditions gives no result/cleanup guarantee or automatic repair. Future export, callback, thread entry, dynamic rounding, and exception observation require new contracts. Normal FP optimization may preserve language results and explicit Abort checks without preserving hardware exception status.

### 21.5.5. Storage, attributes, and unwind information

Keep address-free scalar values in SSA, using phi at joins; mutable locals may use promotable fixed slots. Put required fixed-size allocas in function entry before calls with explicit alignment, while initialization and cleanup stay at their source execution points. Dynamic stack allocation is initially unsupported. Reuse storage only after old destruction responsibility and final references have ended; a completed Move may discharge the old responsibility without destruction. Generic entry reservations follow §21.4.6. Optional lifetime.start/end markers follow actual storage lifetime, not an Origin spelling or Move alone; omit unproven markers.

**Safe value-borrow call contract.** Verify ref/V and uniq/V Loans from receiver/argument formation through the entire call, extended by result uses where required. Check overlaps among arguments, captures and storage anchors, including all direct/indirect static effects, defaults, initialization, cleanup and reentry (§15.6.4). Do not shorten caller protection to the callee's last use. Unknown potentially conflicting effects cause rejection. Owned environments still retain relevant static/capture anchors.

ref/V keeps V's immediate inline storage unchanged throughout the call. uniq/V permits no conflicting independent access; access through its derived child Reborrows remains valid. This common call-wide proof supports noalias for **both** borrow modes, consistently on definitions, calls and adapters with pointer provenance preserved; it requires no separate private-body reproof per call. Passing the same shared reference twice is legal: the modified-memory premise of noalias still applies.

| Attribute on a normal safe value-borrow pointer | Required premise |
| --- | --- |
| nonnull, noundef | Valid initialized borrow pointer |
| align | Proven constant alignment, or a known lower bound for generic V |
| dereferenceable | Positive constant valid byte range; never inferred from zero-size substitute storage |
| readonly for ref/V | No writes to V's inline storage through the argument or derived pointers; not function purity |
| noalias for ref/V and uniq/V | The complete call-wide contract above |

These guarantees cover immediate V storage only. Targets reached through loaded pointers retain their own rights: another rc handle may share its payload and update counts without writing the borrowed handle slot. Array/Slice handle noalias does not prove their element pointers disjoint; vectorization needs separate provenance, range and Loan proofs. Do not apply this table to object-borrow headers, uninitialized internal pointers or an entire environmentWord.

Unsafe/FFI implementations receiving these borrows must satisfy the same promises: raw pointers derived from ref cannot write its inline storage, and uniq permits no conflicting independent access during the call. Diagnose missing Loan/effect verification before emission. Future interior mutability/concurrency requires revisiting the shared proof.

For other optimization attributes, separately prove defined bits/no poison for noundef, including padding in coercions; full GEP conditions for inbounds; and no signed/unsigned overflow for nsw/nuw. Do not apply mustprogress, willreturn or loop.mustprogress uniformly to ordinary functions or loops.

All generated definitions use uwtable(async). Let LLVM produce required .pdata/.xdata, prologue/epilogue and register/stack recovery information, including decisions for optimized leaf functions. This enables OS stack recovery/walking, not language exceptions, Abort cleanup, or foreign unwind permission. Treat nounwind separately; the initial emitter does not infer it merely from LibraryImport's no-unwind contract and generates no exception-catching landingpad. Native assembly needs appropriate Windows unwind information for its own stack/nonvolatile-register operations.

### 21.5.6. Constants

Retain exact integer magnitudes and decimal values through fitting; round once to the selected f32/f64 and emit its fitted bits using exact LLVM 22.1.8 syntax. Do not round through host f64 or culture-dependent formatting. Verify bit equality after LLVM rereading (for example, 0.1 fitted to f32 has bits 0x3DCCCCCD).

For integer constants, validate the fitted value against its language Type before lowering and validate the selected-width bit representation before LLVM serialization. LLVM parser acceptance is not proof that a constant was within range; emission must not rely on implicit truncation to repair an invalid constant plan.

After source newline/escape processing, encode strings as UTF-8 with byte lengths; embedded NUL is data, with no automatic terminator. Preserve interpolation evaluation order. Share identical byte sequences within the module as private unnamed_addr constants; backing addresses do not define string equality/identity. An empty literal is Static/null/length zero with no allocation. Non-Copy ownership remains unchanged (§22.5.5).

```llvm
; UTF-8 for U+3042; length 3, no trailing NUL.
@kimi_text_a = private unnamed_addr constant [3 x i8] c"\E3\81\82", align 1
```

### 21.5.7. Backend support supply

Toolchain storage, native generation/verification, adopted-archive installation and Application build/reference flow are defined together in §20.8.8.

Memory intrinsics may become external libcalls. Supply memcmp, memcpy, memmove, memset and Windows x64 assembly __chkstk from the versioned native COFF static archive **kimi_backend_windows_x64_v1.lib**, logical name **kimi_backend**. Do not generate their loop bodies in .ll or use bitcode/LTO members; keep them outside O2 to avoid recursive libcalls. optnone alone is insufficient. Prefer one strong symbol per archive member.

Memory helpers meet Windows x64 C argument/result contracts, including both overlap directions for memmove. __chkstk uses the backend's special ABI, not an ordinary C function: validate the RAX size input/preservation, stack and register restoration, and guard-page probing against emitted calls. Never disable required probes or substitute a similar name from another ABI. All helpers meet this profile's CPU, MXCSR, and unwind contracts, with no extra external dependencies, CRT startup, or dynamic initialization.

`memcmp(const void*, const void*, size_t) -> int` compares exactly the requested byte range as unsigned bytes and returns a negative, zero or positive value according to the first difference. It performs no writes or allocation and never reads beyond either range. This supplied implementation accepts zero count without accessing either pointer, including null. String equality first compares lengths; string ordering compares the common byte prefix and then lengths. The release tag and padding are not comparison data. Backend supply ABI 2 adds memcmp; the archive filename retains the windows-x64-v1 profile name, while ABI and content identity are checked separately.

Do not emit ssp/sspstrong/sspreq initially. Stack-protector cookies/failure handling and other arithmetic helpers require separately validated supplies before enabling dependent features.

Every Application and Library module, regardless of FP use or optimization, supplies exactly one strong external data definition:

```llvm
@_fltused = global i32 0, align 4
```

This backend marker is not CRT initialization state. No dllimport, weak, or common definition is allowed; other inputs, including the backend archive, must not define it.

The compiler's profile catalog fixes packageId=kimi-backend-windows-x64, abiVersion=2, the actual archive's SHA-256, the profile/LLVM/CPU/FP/unwind contract, and providedSymbols=[__chkstk, memcmp, memcpy, memmove, memset]. packageVersion comes from the shared compiler/backend release in Directory.Build.props (§20.8.7). Update ABI version when symbol/call contracts change. Filename/path equality is insufficient. If version/ABI/hash are not established, do not claim successful generation using a placeholder supply.

Record the archive as a profile-wide link input even when no known reference currently needs it; unused archive members need not link. Generated _fltused and externally supplied symbols have separate manifest classifications (§20.8.3). Later LLVM may add/remove references: inspect actual object undefined symbols and verify providers. Unknown/unsupplied dependencies fail adoption or linking, never receive empty stub helpers. This supply does not add to the six runtime operations or seven Windows APIs.
