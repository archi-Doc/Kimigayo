# 22. Core, program execution, and foreign functions

[Specification index](../SPEC.md)

This chapter defines the required declarations of the Core Kotonoha, process startup and shutdown, the foreign-call boundary, and minimal standard output. Here, Core names the foundation Kotonoha, not a Type component.

## 22.1. Required Core declarations

Every Compilation binds exactly one compiler-compatible **Core Kotonoha**, using the reserved direct reference name Core. The declarations below are public at that Kotonoha's root and available through default aliases. Qualified paths such as `::Core.Option<T>` identify them regardless of local shadowing. Compiler metadata records their originating Kotonoha/version and Symbol Identities; a same-spelled user declaration or replacement alias never receives their special behavior. Reject a missing, duplicate, or incompatible Core definition before finalization. The compiler may synthesize these definitions, but synthesized and loaded definitions must have the same language identities and contracts. Core itself is built with these identities designated by the compiler.

This is the minimal set named by language rules, not a promise of a general standard library:

| Declaration | Required shape or operation |
| --- | --- |
| `Option<T>` | enum with Some(T), None in that order; Self is Copy with condition-atom set {T is Core.Copy} |
| `Result<T,E>` | enum with Ok(T), Err(E) in that order; Self is Copy with condition-atom set {T is Core.Copy, E is Core.Copy} |
| `Weak<S>` | Compiler-managed Non-Copy struct over a valid complete rc/arc S; always holds a target table, with no empty constructor. Core.downgrade / upgrade / clone follow §3.2.2 and §13.5.9 |
| `Array<T>` | Non-Copy owning dynamic sequence over a valid complete T; no Owned requirement; public read-only length/capacity: isize and indices: ResolvedRange; §4.6 indexing, §4.7 mutation/capacity APIs, literals and consuming Iterable conformance |
| `Index` | Copy, Owned, Equatable direction/offset value; constructor, read-only fields, resolve/tryResolve under §4.6.2 and §4.6.4 |
| `Range` | Copy, Owned, Equatable unresolved boundaries; syntax construction, read-only fields, resolve/tryResolve under §4.6.3 and §4.6.4; not Iterable |
| `ResolvedRange` | Copy, Owned, Equatable validated interval; constructor, read-only fields, and `Iterable` with associated Type `Element = isize` under §4.6.3 |
| `Slice<T>` origin source | Copy shared view with all public operations in §4.6.6; implements `Iterable` with associated Type `Element = ref/T from source`; backing Origin is explicit or inferred under ordinary rules |
| `Dictionary<K,V>` | Non-Copy owning collection over valid complete K/V requiring K is Equatable; no Owned requirement; literal construction, existing-key indexing, public read-only length/capacity: isize, §4.7 lookup/mutation/capacity APIs and consuming Iterable conformance |
| `Stringify` | `func stringify(self: ref/Self) -> string`; returns an independent owned string |
| `Equatable` | `func equals(self: ref/Self, other: ref/Self) -> bool` |
| `Comparable: Equatable` | `func compare(self: ref/Self, other: ref/Self) -> i32`; negative/zero/positive for less/equal/greater |
| `Iterator` | `associate Element`; `func next(self: uniq/Self) -> Option<Self.Element>` |
| `Iterable` | `associate Element`; `associate Iterator is ::Core.Iterator`; `Self.Iterator.Element is Self.Element`; `func iterate(self: owner/Self) -> Self.Iterator` |
| Copy, Owned, Callable | Compiler-intrinsic requirement identities with exactly their existing derivation, ownership, and call rules; they are not ordinary user-implementable replacements |
| Object ownership intrinsics | Core.makeObj / makeRc / makeArc, strong and Weak Core.clone, Core.downgrade / upgrade, Core.makeRcCyclic / makeArcCyclic, with §13.5.8–9 names, Types and acquisition contracts |
| `writeLine` | `public func writeLine(text: string) -> ()`; standard-output operation under §22.4, with ordinary owned-argument acquisition |

Iterator and Iterable are static, non-lending Contracts. Their Element requirement is the sole complete-Type exception (§8.4.3) and may bind ref/T from an existing external source; Iterable.Iterator still binds a Core. Table signatures follow normal associated-Type, receiver, result-Origin, and lifetime rules.

Fixed arrays implement Iterable with Element = T. Owning Array/Dictionary iterators retain and destroy unyielded elements in §4.7.6 order. ResolvedRange and Slice use concrete Core iterator identities with §4.6’s element Types and dependencies: range iterators store position/end; Slice iterators store a copied handle, position, and external source Loan. Neither owns yielded elements, and both stay exhausted after None. Dependent Types preserve source dependencies through associated Types and Option payloads. Receiving next’s result extends no lifetime. These requirements add no public iterator constructors or other changes to associated-requirement kinds.

The primitive keyword string denotes the compiler's UTF-8 string Core, not a shadowable alias; its required operations here are literal/interpolation construction, concatenation, comparison, and Stringify. No character indexer, mutable string buffer, allocator, or formatting options are implied. Fixed-array syntax and layout follow [sequence Types](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices); metadata, indexed Place acquisition, and shared reading follow [indexing and slicing](04-arrays-indexing-and-slices.md#46-indexing-and-slicing).

For Option/Result Copy conditions, compare atom sets using §8.7's proposition identity and conjunction elimination. T/E denote the corresponding parameter slots and Core.Copy the recognized Symbol. Order, transparent grouping, and duplicate atoms do not change the set; missing/unconditional Copy, missing/extra atoms, or different identities are incompatible. Retain all other required-shape checks without general logical-equivalence reasoning. Generated sources may use canonical T, E order, but loaded Core definitions cannot be required to use that order.

Option/Result Copy and Owned follow ordinary enum rules; no extra copying is introduced. A changed Core contract invalidates dependent capability, acquisition, and generation results under §21.3.4. Unchanged Case order and payload structure do not establish binary compatibility with older Core artifacts.

Array/Dictionary contents, generic enum payloads, and fixed-array elements preserve complete Type/Origin/Loan dependencies under §15.4. Array's Owned classification follows T, Dictionary's follows K and V, independently of runtime contents; both remain Non-Copy. No container grants permission to hide dependencies or extend a referent's lifetime. Checked-cast designs use the required Core Option Identity despite deferred View syntax. Dictionary need not expose hashing. Dynamic mutation, allocation, ordering, retained dependencies, effects and complexity follow §4.7; further library APIs remain separate designs.

## 22.2. Program startup and static initialization

### 22.2.1. Startup selection

After directive selection, Mods, and Binding, an Application must select exactly one of:

- One SourceDocument with top-level runtime body items.
- One eligible root-level `public func main() -> ()`.

Reject mixed forms, multiple candidate documents/mains, or no candidate. Search the current project, not dependencies; enumeration order and optimization never select the winner. EntrySource is not an initial selection mechanism, and naming an empty document cannot make an Application valid.

Determine HasTopLevelRuntimeBodyItem once per document from selected root items, without descending into functions or Containers. This classification does not require that machine instructions survive:

| Root item | Counts as a runtime body item |
| --- | --- |
| Expression/control expression, unsafe/defer/require statement | Yes, including Unit and removable expressions |
| Local let/var | Yes, even without an initializer |
| Function declaration, including main | No |
| Container or alias | No |
| Attribute argument, Type expression, constant evaluation, Mod execution itself | No |
| Excluded syntax | No |
| Selected legal generated item | Classify the resulting item by these rules |

Top-level bindings remain SourceDocument-local, not static Properties. Container static Properties do not count and keep first-access initialization. Generated items must be legal in their target context; neither generation nor startup classification expands that grammar.

Execute the chosen document's body items in source order with its source scope, CodeContext, local-function visibility, lifetimes, and cleanup. Lower it to a private internal function, without synthesizing public main. This physical function does not create a source-level return target (§14.5.3).

```kimi
// Implicit startup; the uninitialized local itself also counts.
let pending: i32
::Core.writeLine("Hello, world!")
```

A minimal intentionally empty Application is the Unit expression `()`. Empty files and declaration-only files supply no implicit body.

### 22.2.2. Explicit main and Library

An Application's explicit main is exactly lowercase main, public, directly at the source root, with no parameters/receiver, generic or Origin parameters, and Unit result (ordinary result omission is allowed). It is a safe ordinary function with a body, not unsafe, a foreign import, or a specialization. A main inside a group/struct or another function is not a candidate. Root-level public main is shared-root declaration syntax under §6.1.1, retaining declaration-site aliases; public promises no unmangled native symbol or export.

Validate every root-level public main in an Application as a startup signature. Diagnose invalid declarations rather than selecting a convenient overload. Normal Unit return and fallthrough are permitted; no special ownership rules apply.

```kimi
public func main() -> ()
    let message = "Hello, world!"
    ::Core.writeLine(message)
    // message has been moved; using it again is an error.
```

Adding a top-level `::Core.writeLine("Top level")` to this project is an error because it mixes startup forms. Integer-returning main and a safe Core.exit API are not initial features; normal termination is 0 and Abort is 1. Runtime.Exit remains internal.

A Library requires no startup candidate, never automatically calls main, and emits no OS entry. Treat main as an ordinary function without the Application signature restriction. Reject top-level runtime body items, including uninitialized let/var. Initial Library .ll is for inspection, LLVM verification, and object-generation experiments, not an externally callable library/DLL or dependency artifact; all language functions remain internal, even public ones. Optimization may remove all such functions, so inspect pre-optimization IR.

### 22.2.3. OS entry, static initialization, and shutdown

The initial Windows Application emits compiler-reserved external `__kimi_start`, physical signature void (), Windows x64 ccc, noreturn, and the profile attributes (§21.5). Link with /entry:__kimi_start. It performs required runtime initialization, calls the selected body once, completes normal shutdown, and calls Runtime.Exit(0). Empty initialization/shutdown helpers may be omitted. Ordinary mangling prevents source names from colliding with this reserved symbol.

```llvm
; Fragment: profile attributes and internal helper definitions are omitted.
; Each helper has one internal definition, not an additional same-name declare.
define void @__kimi_start() noreturn {
entry:
  call void @__kimi_entry_body()
  call void @__kimi_shutdown()
  call void @__kimi_runtime_exit(i32 0)
  unreachable
}
```

The entry body is the implicit body or a call to the selected main. Add required runtime initialization before it; heap/standard-handle acquisition may instead occur inside each runtime operation.

The entry handles Kimigayo initialization/cleanup; it does not run executable CRT startup or C/C++ static constructors. Foreign initialization must already be satisfied, for example by OS DLL loading, or by an explicitly supported adapter. /NODEFAULTLIB is not initialization.

Static stored Properties initialize per slot under §11.3.2. A first read, Borrow, write, or other storage operation checks:

| State | Action |
| --- | --- |
| Not started | Mark Initializing; evaluate the declaration initializer; after normal completion mark Initialized, then perform the operation |
| Initializing | Abort for an initialization cycle |
| Initialized | Perform the operation without rerunning initialization |

A first write initializes before Replacement. Actual access determines dependency order, not fragment/file/link order. Computed execution initializes only storage actually accessed. A Type/function reference, untaken branch, or effect summary initializes no unrelated Field. Check all initializers even if unused; unused Fields need not initialize and are not destroyed. An implementation without static execution support diagnoses required uses rather than treating this specification as an implementation.

Normal body exit cleans its locals exactly once. Then destroy initialized static values in reverse successful-initialization order, retaining required lifetime dependencies; reject dependencies that cannot survive this order. Initializing new static storage, accessing destroyed storage, or reentering a Field's destruction during shutdown Aborts. These language rules include dependencies, although multi-Kotonoha linking is outside the initial profile.

Cleanup must finish before subsequent cleanup or Exit. Abort stops normal cleanup/unwinding and attempts diagnostics before Runtime.Exit(1) (§17.3, §22.5); secured results are not delivered or separately destroyed afterward.

This revision admits one execution thread, with no source thread creation or concurrent foreign reentry. Atomic arc counts do not expand that permission; synchronization and thread transfer remain §D.2's design boundary.

## 22.3. Foreign function imports

### 22.3.1. Declaration and call contract

`#LibraryImport("library", "symbol")` on a bodyless unsafe func selects the target C calling convention. Both arguments are required nonempty, non-interpolated, NUL-free string literals. The first is an Ordinal logical library key in NativeLibraries (§20.8.2), not a DLL filename/path; the second is the exact external symbol, independently of the source function name.

Allow imports only directly in group/rootgroup or as receiverless struct type functions. Reject receivers, generic/Origin parameters, default/optional arguments, varargs, specializations, and executable bodies. Calls are direct only; unsafe functions cannot be acquired as values. Ordinary access and unsafe-call rules apply.

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public unsafe func observe(record: unsafe/NativeRecord) -> ()
```

NativeRecord can be the C-layout example in §21.1.3; the corresponding C declaration is `void observe_record(NativeRecord *record);`. The raw pointer is not read-only. Layout, validity, lifetime, writes, retention, ownership, and active Loans remain the caller's contract; this example supplies no new pointer-acquisition or raw-storage construction API.

Acquire arguments once from left to right, then use the selected ABI. No automatic marshalling, retention, allocation, freeing, or ownership acquisition occurs. Normal return resumes ordinary cleanup. C++ exceptions, SEH unwind, longjmp, callbacks, and reentry must not cross Kimigayo frames; control handled entirely inside the foreign code is allowed. Violations carry no result or cleanup guarantee. Apply the FP boundary contract in §21.5.4. Do not infer nounwind merely from these source restrictions or generate a landingpad to catch violations.

Unresolved logical libraries and unsupported ABI signatures are compilation errors. Record required link inputs; unresolved native symbols fail manual linking/loading before entry. C aggregate passing, export, callbacks, varargs, and extra calling conventions remain extensions; C storage layout is specified separately and does not enable them.

### 22.3.2. Initial Windows C ABI

Compute a physical signature once and share it between declare and call:

| Kimigayo parameter/result | C value | LLVM Type |
| --- | --- | --- |
| i8 / u8 | int8_t / uint8_t | i8 |
| i16 / u16 | int16_t / uint16_t | i16 |
| i32 / u32 | int32_t / uint32_t | i32 |
| i64 / u64 | int64_t / uint64_t | i64 |
| f32 / f64 | float / double | float / double |
| unsafe/T | Corresponding data pointer | ptr, address space 0 |
| Unit, result only | void | void |

Use ccc with no signext, zeroext, inreg, byval, or sret for these entries. Do not widen i8/i16 to i32 or apply vararg default promotions. Other numeric conversions are separate language operations. LLVM handles registers, stack arguments beyond the fourth, shadow space, and stack alignment; unused upper bits are not meaningful. Additional optimization attributes need independent proof.

```llvm
declare dllimport i8 @native_i8(i8)
declare dllimport i16 @native_u16(i16)
```

Exclude bool, char, string, borrows, object handles, aggregates (including C-exchangeable structs/arrays), function/closure values, i128/u128, and isize/usize. Raw pointees are not passed by value and need not be C-exchangeable when opaque. Future aggregate passing needs separate argument/result C ABI classification and tests, not direct translation to LLVM aggregate parameters.

## 22.4. Minimal console output

Core provides the public ordinary function `writeLine(text: string) -> ()` at its root, available through the Core default alias. `::Core.writeLine` identifies the required Symbol regardless of local shadowing. It is a safe, nongeneric function with one required owned string argument and no receiver, defaults, formatting parameters, or result borrow. The compiler/runtime supplies its implementation; it is not a source LibraryImport taking a string and does not expand the FFI Type surface.

Acquire text once under Copy/Move rules and write all its UTF-8 bytes plus one LF to standard output. NUL is data. Preserve contents without normalization, CRLF conversion, or locale encoding. Failure may leave partial output; no rollback or device atomicity is promised. Return Unit after host acceptance and flushing this call’s runtime buffer, not necessarily display or durable storage. Failure to complete initiates Abort under normal diagnostic/termination rules, even if stdout is unavailable. Destroy the acquired argument on normal return. Diagnose an unsupported target/feature if the backend cannot provide this operation.

**Complete application example (implicit startup in one SourceDocument).**

```kimi
::Core.writeLine("Hello, world!")
```

The required standard-output bytes are UTF-8 `Hello, world!` followed by LF; normal completion exits with code zero under §22.2. No source main function, user alias, unsafe block, interpolation, or user-declared foreign function is needed. Passing an existing string local Moves it; use its Stringify mapping to obtain an independent owned string when reuse is needed. Borrowed output overloads and general I/O error/result APIs remain outside this minimal operation.

The initial Windows implementation of this operation is specified in §22.5; output settings, manifest, and manual build steps are in §20.8. The first executable implementation milestone and the distinction between existing and proposed settings are recorded in [STATUS.md](../STATUS.md#c12-first-executable-milestone). A prototype supporting only that subset must identify itself as partial; the milestone does not relax the Core identity/shape or validation requirements of a fully conforming Compilation. Unused executable Core bodies need not be emitted, but a same-spelled stub without the required identity and contract is not a compatible Core definition.

## 22.5. Initial Windows runtime

### 22.5.1. Operations and source context

The compiler emits these six logical operations in the same LLVM module. They are internal abstractions, not source APIs or a dedicated runtime DLL:

```text
Alloc(size: usize) -> ptr
Free(memory: ptr) -> ()
WriteStdout(data: ptr, length: usize) -> ()
Exit(code: u32) -> Never
TryWriteStderr(data: ptr, length: usize) -> bool
Abort(reason: DiagnosticText, sourceLocation: SourceLocation) -> Never
```

Alloc, detected Free failures, and WriteStdout fail by Abort. TryWriteStderr returns false without initiating Abort. Abort attempts diagnostics then Exit(1); Exit never returns and performs no Kimigayo cleanup. The normal language path calls Exit(0) only after cleanup.

Physical helpers carry any required private diagnostic context using the compiler-selected internal ABI (§21.4.2); its position and representation are not fixed. Lowering preserves static logical path/line/column information for the original operation, including failures inside Alloc/Free/WriteStdout and generated-source CodeContext provenance. This does not depend on PDBs or stack traces.

Generated arithmetic checks report the start of the failing arithmetic expression. Compound assignment and increment/decrement report the start of the complete update expression. These locations remain the same across optimization levels.

### 22.5.2. Allocation and release

Use the process heap, MaxObjectSize = 2^63 - 1, and alignment support up to 16. Check length * stride, headers, and alignment rounding before allocation; overflow/limit failure Aborts. Alloc checks its own limit too, obtains GetProcessHeap, and calls HeapAlloc(heap, 0, max(size, 1)); null heap/allocation Aborts. It returns uninitialized raw memory, not an Initialized language value. The substitute byte for size zero does not change Type size/stride.

Free(null) succeeds without work. Otherwise require the original live pointer returned by this allocator, never an interior pointer or literal backing. GetProcessHeap failure or detected HeapFree(heap, 0, memory) failure Aborts. Lowered cleanup runs destruction before Free; Free itself invokes no destructor. Correct ownership/pointers are a static-analysis and generation duty; detection of double frees or arbitrary corruption is not guaranteed.

HeapAlloc flags remain zero, without exception generation or disabling process-heap synchronization. HeapAlloc does not supply last-error on failure; do not report a stale GetLastError value for null allocation. Obtain last-error immediately after APIs that provide it, including failed HeapFree/WriteFile.

### 22.5.3. Synchronous byte output

WriteStdout and TryWriteStderr share a checked byte-write adapter. They add no newline, NUL scan, encoding conversion, buffer, or pointer retention. Length zero succeeds before handle acquisition or pointer access. For positive length, require length <= MaxObjectSize, nonnull data, a readable live range within one allocation, and no unsigned 64-bit overflow in baseAddress + (length - 1). Numeric checks do not prove allocation validity/lifetime.

Use GetStdHandle(-11) for stdout and -12 for stderr; null or INVALID_HANDLE_VALUE fails. Do not close these handles. Initial support requires synchronous standard handles. WriteFile receives a 32-bit written-count slot and null OVERLAPPED; capture a supplied last-error before calling another API.

```text
remaining = length
while remaining > 0:
    chunk = min(remaining, UINT32_MAX)
    request chunk bytes with WriteFile
    fail if the call fails, written == 0, or written > chunk
    remaining -= written
    if remaining > 0:
        advance within the original buffer with checked pointer arithmetic
```

Do not compute a next pointer after completion. Partial writes advance by actual progress; never retry zero progress indefinitely. WriteStdout turns any detected failure into Abort; TryWriteStderr returns false and must not call Abort, WriteStdout, or Alloc. Output may be partial, with no rollback, atomicity, display, durability, or bounded synchronous-wait guarantee. Success means OS acceptance of all bytes. These checked buffer rules do not change general unsafe pointer arithmetic.

### 22.5.4. Abort diagnostics and exit

Fixed diagnostics use an ASCII identifier, English reason, and source location, optionally a valid numeric OS error:

```text
Main.kimi:3:5: abort KIMI_E_STDOUT: Failed to write to stdout (win32=6)
```

Use unique catalog codes, including KIMI_E_ALLOC_SIZE (allocation size exceeds limit), KIMI_E_PROCESS_HEAP, KIMI_E_ALLOC, KIMI_E_FREE, KIMI_E_STDOUT, KIMI_E_INT_OVERFLOW (Integer overflow), KIMI_E_INT_DIV_ZERO (Integer division or remainder by zero), KIMI_E_INT_SHIFT_COUNT (Shift count out of range), KIMI_E_INT_CONVERSION (Integer conversion out of range), and KIMI_E_INDEX_BOUNDS (Index out of bounds). Integer division/remainder by zero uses KIMI_E_INT_DIV_ZERO; signed minimum with divisor -1 uses KIMI_E_INT_OVERFLOW for both operations. A shift count outside `0 <= count < left operand bit width` uses KIMI_E_INT_SHIFT_COUNT. Discarded left-shift bits do not trigger overflow. A runtime integer conversion outside the target range uses KIMI_E_INT_CONVERSION; direct literal fitting failures remain compile-time errors. Ordinary element indexing outside the receiver bounds uses KIMI_E_INDEX_BOUNDS, including constant indices and zero-length arrays. Omit unavailable OS codes; do not use FormatMessageW. In displayed logical paths, escape non-ASCII/control characters as `\u{HEX}` and backslash as `\\`; preserve the actual path/provenance internally.

Output diagnostics from constants, valid input strings, and small fixed work areas without requiring heap allocation or one concatenated dynamic string. Explicit `$abort(expression)` retains ordinary one-time argument evaluation and outputs the resulting UTF-8 string after KIMI_E_ABORT without translation or escaping its contents. Once Abort starts, do not normally destroy that string.

TryWriteStderr failure truncates diagnostics and proceeds to Exit(1), with no recursive diagnostic path. Runtime.Exit forwards u32 to ExitProcess and ends its caller block with unreachable. It runs no cleanup; successful shutdown and Abort reach it through their distinct §22.2 paths.

### 22.5.5. String handle and writeLine

The initial internal string storage representation is `{ ptr, i64, i8 }`: data, byteLength, releaseKind. Use DataLayout for padding/alignment. Function passing is selected independently under §21.4.2 and need not use aggregate argument/result slots. This storage representation is not a public FFI/binary ABI.

| Component/state | Validity and responsibility |
| --- | --- |
| Length | 0..MaxObjectSize; contents are valid UTF-8 |
| Positive length | Nonnull readable data with the required lifetime |
| Static = 0 | Compiler constant backing, never freed; null permitted only at length zero |
| Heap = 1 | Nonnull original live Runtime.Alloc pointer, capacity >= length, exactly one owning release responsibility |
| Empty Heap | Keep and free the original pointer even at length zero; allocation-free empties use Static |
| Other releaseKind | Invalid; destruction's default branch Aborts instead of freeing an unknown pointer |
| Moved source slot | Unusable as a value; no bit clearing or rewriting is required |

Establish validity at construction, not by revalidating UTF-8/allocations on every use; corruption detection is not guaranteed. Literal backing may be shared without granting Copy to string. Hello world needs no heap allocation.

The required Core.writeLine Symbol (§22.4) acquires its owned argument once, calls WriteStdout(data, length), calls WriteStdout on a one-byte LF constant, then normally destroys the argument and returns Unit. Static release does nothing; Heap release calls Free. An empty string still emits LF. Output failure Aborts without normal argument destruction; earlier output is not rolled back. No concatenation buffer is required.

Write raw UTF-8 bytes to redirected files/pipes without changing the console code page. Non-ASCII console appearance depends on console configuration; universal Unicode console display is not initially guaranteed. A GetConsoleMode/WriteConsoleW adapter is a future extension, not implicit UTF-16 output.

### 22.5.6. Windows external symbols

WindowsRuntimeSymbols retains each external name, physical signature, calling convention, dllimport setting, and link input. Share declarations through the module symbol table (§21.5.2), including matching LibraryImport declarations. Emit only needed APIs, using Windows x64 ccc:

```llvm
declare dllimport ptr @GetProcessHeap()
declare dllimport ptr @HeapAlloc(ptr, i32, i64)
declare dllimport i32 @HeapFree(ptr, i32, ptr)
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport i32 @GetLastError()
declare dllimport void @ExitProcess(i32) noreturn
```

HANDLE and data pointers use ptr, SIZE_T i64, DWORD/UINT/BOOL i32, and LPDWORD ptr to a 32-bit slot. Windows BOOL is not i1. Empty parameter parentheses mean no parameters, not omitted Types. dllimport specifies reference generation, not automatic linking; use the kernel32 input (§20.8.2).
