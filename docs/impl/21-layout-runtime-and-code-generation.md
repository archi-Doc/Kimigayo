# 21. Layout, runtime, and code generation

[Implementation specification index](../IMPL.md)

This chapter defines the windows-x64 target profile: the layout and foreign-function guarantees that programs may rely on, the code generation policy and the observable runtime behavior. Everything else about generated code is implementation-defined.

## 21.1. Storage layout

### 21.1.1. Common rules

For a storable Type `T`, `size(T)` is the number of inline bytes it reserves, including padding; `alignment(T)` is a positive power of two; and `stride(T)` is the size rounded up to the alignment. These are notation, not source operators. On windows-x64 sizes, strides and Field offsets must not exceed 2^63 − 1; a larger layout is a Language error at the use that needs it ([§8.15.3](../spec/08-generics-constraints-and-contracts.md#8153-concrete-layout-and-representation)).

- Every component meets its natural alignment, and an aggregate's alignment is at least the maximum of its components'. Positive-sized components never overlap, and padding of a nested component is never reused.
- Padding has no defined content. Byte transfers may copy it; typed reads and comparisons never treat it as a value.
- Zero-sized values keep their evaluation, initialization, ownership, Loans and destruction. Distinct zero-sized Places may share an address.
- Physical layout never changes Field identity, initialization order, Partial Move, Loans or destruction order.
- The same compiler, target, settings and selected sources produce the same layout.

A struct stores its own Fields and its direct inline base; computed members add no storage. Borrows, raw pointers and object handles do not embed their referents. An inline storage cycle is a Language error at its declaration ([§6.2](../spec/06-declarations-and-containers.md#62-structure-declarations)).

### 21.1.2. Layout modes

`#Layout` acceptance is defined in [§6.7](../spec/06-declarations-and-containers.md#67-attributes).

**Kimigayo mode.** The physical Field order, padding and alignment sorting are implementation-defined, subject to the common rules. Tuples, enum Case payloads and Closure captures are laid out the same way. No offsets, C compatibility or ABI stability across compiler versions are promised.

**C mode** follows the Windows x64 MSVC layout with packing 16 (`/Zp16`), without `pragma pack`, explicit alignment or bit-fields. Fields are placed in written order, each at the next offset aligned to `min(alignment(F), 16)`; the struct's alignment is the maximum Field alignment, and its size and stride are the end offset rounded up to that alignment. Nested Types keep their own modes. C layout describes the struct's own storage, not an object handle or its header.

**C-mode restrictions.** The restrictions of [§6.7.1](../spec/06-declarations-and-containers.md#671-layout) apply.

```kimi
#Layout("C")
struct NativeRecord
    var kind: u8       // offset 0
    var value: u64     // offset 8
    var flags: u8      // offset 16; size and stride 24, alignment 8

#Layout("C")
struct Tagged
    var tag: u8
    var nothing: ()

func keep(value: Tagged) -> Tagged => value@move   // Error: Tagged's C layout has a zero-sized Field.
```

The C equivalent of `NativeRecord` is `struct { uint8_t kind; uint64_t value; uint8_t flags; }`. C mode alone defines no native export, aggregate argument passing or serialization.

### 21.1.3. Scalars, borrows and handles

windows-x64 is little endian with 64-bit pointers; the built-in Condition `pointerWidth` ([§19.2](../spec/19-compile-time-directives.md#192-conditions)) is 64.

| Type | Size / alignment / stride, bytes |
| --- | --- |
| `i8` / `u8`, `bool` | 1 / 1 / 1 |
| `i16` / `u16` | 2 / 2 / 2 |
| `i32` / `u32`, `f32`, `char` | 4 / 4 / 4 |
| `i64` / `u64`, `isize` / `usize`, `f64` | 8 / 8 / 8 |
| `i128` / `u128` | 16 / 16 / 16 |
| `raw/T`, `ref/T`, `uniq/T`, object handles, `Weak<S>` | 8 / 8 / 8 |
| Unit, `Loan<T>`, Never | 0 / 1 / 0 |

- A `char` stores a Unicode scalar value as an unsigned 32-bit integer. Neither `char` nor `bool` corresponds to C `char`, `WCHAR` or Windows `BOOL`.
- A Type that stores Never inline is laid out as if Never were zero-sized, so the C-mode restriction on zero-sized Fields applies to it.
- `ref/V` and `uniq/V` are one nonnull pointer to the immediate storage of `V`, with no length, count or Loan data. A borrow of a zero-sized `V` holds a nonnull address aligned for `V`.
- **Nonnull Option representation.** `Option<R>`, where `R` is `ref/T`, `uniq/T`, `obj/T`, `rc/T`, `arc/T`, `objref/T` or `objuniq/T`, is one pointer-sized word: null is `None`, and every valid value of `R` is `Some`. It does not extend to `Option<Option<R>>`, to structs or Tuples containing references, or to other enums.
- A common function value is 16 bytes with alignment 8. An environment of at most 8 bytes and alignment at most 8 is stored inline; a larger one is allocated when the value is formed ([§7.7.4](../spec/07-functions-and-callable-values.md#774-function-references-and-erasure)).
- The layout of enums other than the nonnull Option, the string representation, object headers, reference-count storage, weak side tables, the maximum reference count, and all runtime metadata (type descriptors, value metadata, virtual-dispatch tables) are implementation-defined, module-local and not an external ABI. The lifecycle of counted objects, including the Abort at the count maximum, is defined in [§13.6.2](../spec/13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle) and [§13.6.3](../spec/13-operators-and-assignment.md#1363-weak-references).

```kimi
let some: Option<ref/i32> = .None   // One 8-byte word holding null.
let pair: (u8, u64) = (1, 2)        // Layout implementation-defined.
```

## 21.2. Foreign function ABI

The allowed `#LibraryImport` signature Types and their C meanings are defined in [§22.6](../spec/22-core-execution-and-foreign-functions.md#226-foreign-function-imports). On windows-x64 an import uses the Windows x64 C calling convention (LLVM `ccc`) with no `signext`, `zeroext`, `inreg`, `byval` or `sret`:

- `i8` and `i16` are passed at their own width, never widened by vararg promotion; unused upper register bits have no meaning.
- `ref/T`, `uniq/T` and `raw/T` pass the referent's address. `Option<ref/T>` and `Option<uniq/T>` use the nonnull Option representation ([§21.1.3](#2113-scalars-borrows-and-handles)): `None` is the null pointer.
- A Unit result is C `void`.
- `dllimport` and the native supply follow the library kind ([§20.4](20-compilation-configuration.md#204-native-libraries)).

At every call into and return from foreign code, the floating-point control state is the Windows x64 default: round to nearest even, denormals not flushed (DAZ and FTZ off), FP exceptions masked. Foreign code that changes it restores it before returning; the runtime never repairs it. Status flags left by Kimigayo code are unspecified. Generated code has no exception handlers (see [§22.6](../spec/22-core-execution-and-foreign-functions.md#226-foreign-function-imports) for the no-unwinding promise).

The names `_fltused`, `__chkstk`, `memcmp`, `memcpy`, `memmove` and `memset`, and names starting with `__kimi_` or `llvm.`, are reserved. When the program is built for windows-x64, a `#LibraryImport` of a reserved name is an error at native resolution, and so are imports of one external symbol that disagree in signature or providing library ([§20.4](20-compilation-configuration.md#204-native-libraries)).

```kimi
group Native
    #LibraryImport("observer", "inspect")
    public func inspect(value: Option<ref/i64>, mode: i16) -> i32
// C: int32_t inspect(const int64_t *value, int16_t mode);
// Native.inspect(.None, 1) passes NULL.
```

## 21.3. Code generation

### 21.3.1. Generation policy

The implementation monomorphizes: after explicit-specialization selection ([§8.13](../spec/08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization)) it generates a separate body for each closed instantiation that the output requires. Checks cover the whole checked set of [§8.15](../spec/08-generics-constraints-and-contracts.md#815-generic-body-checking), whether or not code is generated for it.

The internal function ABI (parameter passing, hidden arguments, result storage, calling conventions between Kimigayo functions), symbol names and optimization attributes are implementation-defined and may change between builds; independently generated modules have no binary compatibility. A `public` declaration is not a native export. The O0 and O2 optimization levels never change acceptance, results, effects, Abort codes or locations, cleanup or nontermination.

```kimi
func identity<T>(value: T) -> T => value@move

let a = identity(1)       // Generates identity<i32>.
let b = identity("x")     // Generates a separate body, identity<string>.
```

### 21.3.2. Limits

Analysis and generation limits (instantiation growth, context depth, default-expansion count, analysis storage, string-interpolation nesting) are implementation-defined. Exceeding one is a Resource diagnostic ([§23.1.1](../spec/23-compiler-services.md#2311-categories)) at the affected declaration, call or token; it never makes the program semantically invalid and does not stop independent checks. Other layout failures are classified by [§8.15](../spec/08-generics-constraints-and-contracts.md#815-generic-body-checking) and [§23.1.1](../spec/23-compiler-services.md#2311-categories); inline storage cycles are not generation limits ([§21.1.1](#2111-common-rules)).

### 21.3.3. Checked operations

Arithmetic, conversion, shift, indexing and range checks Abort as the language rules require ([§13.3.1](../spec/13-operators-and-assignment.md#1331-built-in-numeric-operators), [§13.5.4](../spec/13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions), [§17.4.1](../spec/17-failure-handling.md#1741-causes)), with the codes of [§22.5.2](../spec/22-core-execution-and-foreign-functions.md#2252-abort-codes). A check is omitted only when its success is proven; a check whose failure is proven is replaced by the Abort.

**128-bit subset.** `i128`/`u128` storage, comparisons, bit operations, shifts, integer conversions and checked add, subtract, negate, increment, decrement and multiply are supported. Division, remainder and conversions to or from `f32`/`f64`, including compound assignments, are Unsupported ([§23.1.1](../spec/23-compiler-services.md#2311-categories)), reported at the concrete use for every instantiation of the checked set ([§8.15](../spec/08-generics-constraints-and-contracts.md#815-generic-body-checking)), including unexecuted branches. Required compile-time evaluation ([§17.4.4](../spec/17-failure-handling.md#1744-builds-and-compile-time-evaluation)) and direct literal conversions ([§13.5.4](../spec/13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)) of these operations are supported.

```kimi
func half(value: i128) -> i128 => value >> 1    // Supported.
func third(value: i128) -> i128 => value / 3    // Unsupported on windows-x64.
```

Floating-point operations use IEEE semantics without fast-math, reassociation or fusion. Stack exhaustion is detected with stack probes ([§17.4.3](../spec/17-failure-handling.md#1743-termination-and-cleanup)).

## 21.4. Windows x64 runtime

### 21.4.1. Target

The windows-x64 profile targets `x86_64-pc-windows-msvc` with the baseline x86-64 CPU and SSE2; no other CPU feature is assumed. Generated functions carry Windows x64 unwind information for operating-system stack walking; it implements no language exception or cleanup. The toolchain is resolved as [§20.3.2](20-compilation-configuration.md#2032-builds-and-artifacts) describes.

### 21.4.2. Process entry and exit

An Application defines the external entry `__kimi_start` (linked with `/entry:__kimi_start`) and uses no C runtime startup. It performs runtime initialization, runs the program body ([§22.4](../spec/22-core-execution-and-foreign-functions.md#224-program-startup-and-static-storage)), completes shutdown and calls `ExitProcess(0)`. A Library defines no entry.

An Abort ([§17.4.3](../spec/17-failure-handling.md#1743-termination-and-cleanup)) writes one report to standard error and then calls `ExitProcess(1)`:

```text
path:line:column: abort CODE: Reason
```

Write the `path:line:column: abort CODE: ` prefix, the Reason bytes unchanged, and a final LF. A message may contain line breaks or NUL; neither is escaped or removed. When the operating system supplied an error code, append ` (win32=N)` after the Reason and before the final LF.

- `line:column` follows [§2.1](../spec/02-source-and-lexical-structure.md#21-source-text-and-lines); the code and reason come from [§22.5.2](../spec/22-core-execution-and-foreign-functions.md#2252-abort-codes), and the location from [§22.5.1](../spec/22-core-execution-and-foreign-functions.md#2251-abort-location).
- The logical source path is displayed with non-ASCII and control characters escaped as `\u{HEX}` and backslash as `\\`.
- If writing to standard error fails, the report is truncated and the process still exits with code 1.

```kimi
group Counter
    public func next(value: i32) -> i32 => value + 1

let n = Counter.next(2147483647)
// Main.kimi:2:44: abort KIMI_E_INT_OVERFLOW: Integer overflow
```

### 21.4.3. Memory

Allocation uses the process heap (`HeapAlloc`, `HeapFree`). Every heap allocation, including every `Raw.allocate` result of nonzero size, is aligned to 16 bytes. The largest alignment `Raw.allocate` supports is 16 ([§5.6](../spec/05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations)). The maximum object size is 2^63 − 1 bytes. A zero-byte `Raw.allocate` does not allocate. Allocation, release and size failures Abort with the codes of [§22.5.2](../spec/22-core-execution-and-foreign-functions.md#2252-abort-codes).

```kimi
let p = Raw.allocate<u64>(4)    // 32 bytes, 16-byte aligned on windows-x64.
unsafe => Raw.release(p)
```

### 21.4.4. Console output

`Console.writeLine` ([§22.7](../spec/22-core-execution-and-foreign-functions.md#227-console-output)) writes to the standard output handle (`GetStdHandle`) with `WriteFile`, without a NUL terminator. Redirected files and pipes receive the bytes unchanged; how a console displays non-ASCII text depends on its configuration. Writing to a standard handle opened for overlapped I/O has implementation-defined results (the test runner uses synchronous handles, [§20.6.3](20-compilation-configuration.md#2063-case-execution)).

```kimi
Console.writeLine("ありがとう")   // A pipe receives 15 UTF-8 bytes and LF.
```

### 21.4.5. Elapsed time and timers

`Time.Stopwatch` ([§22.8.2](../spec/22-core-execution-and-foreign-functions.md#2282-stopwatch)) and the task timers use `QueryPerformanceCounter` through the `Kimi.Windows` imports ([§22.8.3](../spec/22-core-execution-and-foreign-functions.md#2283-windows-counter-functions)). The counter frequency is cached in a Kimi-internal static `let` initialized on first use. On windows-x64 a failed `QueryPerformanceCounter` or `QueryPerformanceFrequency` call, or a negative counter value, is a clock failure (`KIMI_E_CLOCK`, [§22.5.2](../spec/22-core-execution-and-foreign-functions.md#2252-abort-codes)).

### 21.4.6. Tasks

How tasks are compiled and scheduled is implementation-defined within the guarantees of [§22.3](../spec/22-core-execution-and-foreign-functions.md#223-asynchronous-tasks) and [§24](../spec/24-suspension-and-asynchronous-tasks.md#24-suspension-and-asynchronous-tasks). This implementation runs one executor per thread, kept across `run` calls, with one I/O completion port; ready tasks resume in FIFO order (resume order is unspecified, [§22.3.2](../spec/22-core-execution-and-foreign-functions.md#2232-running-and-structured-operations)).

I/O waits realize the commit rules of [§22.3](../spec/22-core-execution-and-foreign-functions.md#223-asynchronous-tasks) with the completion port. A handle is associated with `FILE_SKIP_COMPLETION_PORT_ON_SUCCESS` where possible; an operation that completes immediately on such a handle commits at the call, and otherwise its completion packet commits the wait. A cancellation request calls `CancelIoEx`; the wait keeps its registration until the packet arrives and commits to cancellation only when the packet reports `ERROR_OPERATION_ABORTED`.

In steady state, task calls, waits, timers, channel operations, `shield` and repeated `run` calls allocate no heap memory; started children use pooled records.

### 21.4.7. Native imports and backend support

The runtime imports `kernel32` functions such as `GetProcessHeap`, `HeapAlloc`, `HeapFree`, `GetStdHandle`, `WriteFile`, `GetLastError` and `ExitProcess`; the reserved `kernel32` supply is defined in [§20.4](20-compilation-configuration.md#204-native-libraries).

The profile's backend support archive supplies `memcmp`, `memcpy`, `memmove`, `memset` and `__chkstk`, and every module defines `_fltused`. Toolchain adoption verifies that generated code needs no unsupplied backend helper; a missing helper fails adoption, never the linking of an individual program.
