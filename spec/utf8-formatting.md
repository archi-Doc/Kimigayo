# UTF-8 formatting and interpolation

This normative profile is part of §22. It owns UTF-8 formatting, the standard byte buffers and their compiler and runtime requirements. General argument acquisition (§10.2), Origins (§15.3–4), Loans (§15.6), destruction (§16) and failure propagation (§17) still apply.

## 1. Contracts and declarations

`Utf8Format` is the sole formatting Contract. `Stringify` is an ordinary identifier, with no required declaration or fallback. Formatting uses UTF-8 and the default representation only; it adds no alignment, precision, radix, locale, runtime format strings, numeric `@string` conversion or change to string `+`.

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

Both are ordinary, statically checked, user-implementable Contracts. No runtime Contract View, boxing or allocation is required. The result Origin of `reserve` elides to `self`. The outer borrow of `Utf8Writer` and its `target` are independent input Origins; well-formedness requires the target to outlive that outer borrow.

### 1.1. Placement and metadata

The following declarations are required. The default Kimi alias makes `Text` visible without recursively opening its members.

| Container | Declaration | Classification and dependency |
| --- | --- | --- |
| `Kimi` | `Utf8Format`, `BufferWriter` | Static Contracts above |
| `Kimi` | `BufferFull` | Ordinary, stateless Copy struct; public `init()` |
| `Kimi` | `WriteWindow {source}` | Verified intrinsic, Non-Copy; covariant `source`, required Loan `uniq`; opts out of ObjectPayload (§8.4.7.2) |
| `Kimi` | `Utf8Writer {target}` | Verified intrinsic, Non-Copy; covariant `target`, required Loan `uniq`; opts out of ObjectPayload |
| `Kimi.Text` | `FixedBuffer {source}` | Verified intrinsic, Non-Copy; covariant `source`, required Loan `uniq`; opts out of ObjectPayload |
| `Kimi.Text` | `HeapBuffer` | Ordinary Non-Copy struct owning a growable allocation |
| `Kimi.Text` | `Utf8Slice {source}` | Ordinary Copy struct with a private `Slice<u8>`; required Loan `ref` |
| `Kimi.Text` | `InvalidUtf8` | Ordinary, stateless Copy struct; public `init()` |

Only the error types expose initializers; the other types are obtained through the operations below. Intrinsic Origin slots and permissions are fixed compiler metadata (§15.3.5), and neither raw pointers nor a source-declared phantom Origin can reproduce their authority. Window, view and adapter management performs no heap allocation, reference-count update or management callback. The three ObjectPayload opt-outs forbid making these Loan-bound adapters object payloads; the no-allocation requirement is a separate requirement on the operations.

### 1.2. Effects and erasure

`BufferWriter.reserve` may use only authority supplied through `self`; it cannot acquire access to mutable state from the ambient environment. Conformance checking (§8.4.5) rejects an implementation whose transitive effect summary includes access through a mutable static Field, including called functions, lazy initialization or destruction, or a call with unknown effects. Access through a borrowed Field of `self` uses ordinary Loan checking, even when its referent has static storage.

`Utf8Writer` erases the concrete Writer Type. Standard `FixedBuffer` and `HeapBuffer` reservations use direct calls, including inside a non-generic `format` body, and a user Writer uses one function-pointer call per reservation. The implementation may inline capacity checks and share growth and copy routines. Different Writer Types do not require separate `format` instances.

The adapter neither owns, moves nor exposes the Writer, and invokes only `reserve`; the original value keeps its Loans. Well-formedness of `uniq/W during target` requires all dependencies of `W` to outlive `target`, and erasure cannot extend that lifetime. Erased-call effects are checked against the Contract's fixed upper bound, without per-value effect tracking or runtime lifetime or borrow tags.

## 2. Text operations

All lengths and capacities are byte counts of Type `isize`.

```kimi
group Text
    public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer
    public func heap(capacity: isize) -> HeapBuffer
    public func writer<W>(destination: uniq/W) -> Utf8Writer
        W is BufferWriter
    public func utf8(text: ref/string) -> Utf8Slice
    public func validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice{r}, InvalidUtf8>
        origin r.source == bytes.source
    public func toString<T>(value: ref/T) -> string
        T is Utf8Format
    public func tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8])
        -> Result<Utf8Slice{r}, BufferFull>
        T is Utf8Format
        origin r.source == destination
```

| Function | Behavior |
| --- | --- |
| `fixed` | Exclusively borrows an initialized array; committed length zero, capacity N. |
| `heap` | Committed length zero, capacity at least the request. Zero starts unallocated. Negative capacity Aborts with `KIMI_E_ARG_RANGE`; excessive size or allocation failure uses the existing allocation Aborts. |
| `writer` | Creates an adapter with no recorded failure. |
| `utf8` | Borrows valid string bytes without allocation, copying or validation. |
| `validateUtf8` | Validates all bytes once, without allocation or copying. |
| `toString` | Produces an owning string through the internal formatting path (§5); `BufferFull` Aborts with `KIMI_E_FORMAT`. This is also the standard string-copy operation; it does not change `Intrinsics.clone`. |
| `tryFormat` | Formats once through a local FixedBuffer and adapter, then consumes the buffer with `intoText()`. The fresh buffer remains fully validated, so conversion cannot fail or scan. No length pre-pass or retry. On `BufferFull`, partial bytes may remain in the array but are not returned. |

The `tryFormat` result retains the original exclusive Loan, although the returned view requires only shared access. While that result remains live, the source can be read only through the view, not directly accessed or modified. An Origin equality neither creates nor ends a Loan.

```kimi
var bytes: [3 of u8] = [3 of 0]
let result = Text.tryFormat(123, bytes@uniq)
// let first = bytes[0]  // Error: result still retains the exclusive Loan.
match result@move
    .Ok(let text) => Console.writeLine(text)
    .Err(_) => Console.writeLine("too small")
```

### 2.1. Standard buffers

Both buffers implement `BufferWriter` and expose the following operations:

| Operation | Receiver | Result and behavior |
| --- | --- | --- |
| `length`, `capacity` | getter, `ref/Self` | Committed byte count and total capacity. |
| `bytes()` | `ref/Self` | `Slice<u8>` of committed bytes, dependent on the receiver borrow. |
| `text()` | `ref/Self` | `Result<Utf8Slice, InvalidUtf8>`; validates the unvalidated suffix without recording success. The result depends on the receiver borrow. |
| `validate()` | `uniq/Self` | `Result<(), InvalidUtf8>`; records validation on complete success only. |
| `clear()` | `uniq/Self` | Resets committed and validated lengths to zero, retaining capacity; does not promise erasure. |
| `reserve(minimum: isize)` | `uniq/Self` | `Result<WriteWindow, BufferFull>` as in §3. |
| `intoText()` (FixedBuffer) | `owner/Self` | `Result<Utf8Slice{r}, InvalidUtf8>`, `origin r.source == self.source`. Validates the remaining suffix and returns the committed source region. Consumes the buffer on success or failure without freeing the source. |
| `intoString()` (HeapBuffer) | `owner/Self` | `Result<string, InvalidUtf8>`. Validates the remaining suffix, then transfers the allocation pointer, length and sole release responsibility without copying. On failure the consumed buffer is destroyed. |

An unallocated empty HeapBuffer becomes a Static string. An allocated buffer retains its original pointer and Heap release kind even when its length is zero. Optional in-place shrinking is specified in §3.4.

`intoText()` depends on the original array, not the consumed local buffer, and keeps the original exclusive Loan. It can return a view that depends on the caller's array, but cannot let local source storage escape. Conflicting views, Windows and adapters prevent mutation, movement, growth and destruction. No general mutable Slice is introduced.

### 2.2. UTF-8 views

`Utf8Slice.length` is a getter with `self: Self`. `bytes(self: Self) -> Slice<u8>{r}` has `origin r.source == self.source`. Both preserve the source borrow without reference counting.

Validation accepts exactly UTF-8 encodings of Unicode scalar values. It rejects truncated sequences, overlong encodings, surrogates and out-of-range values, without replacement or normalization. NUL is data. An arbitrary byte subrange is not presumed valid UTF-8.

### 2.3. Adapter operations

```kimi
func write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>
    T is Utf8Format
func status(self: ref/Self) -> Result<(), BufferFull>
```

These are members of `Utf8Writer`. `write` handles all formattable values, including strings, views and characters (§4). `status` returns the recorded failure, if any. There is no reset, raw Window, underlying Writer access or ownership extraction. To resume after failure, end the old adapter's uses, optionally clear the buffer, and create a new adapter. Neither output nor side effects are rolled back.

Ordinary method calls, including calls through function values, evaluate arguments normally. Only `$tryWrite` short-circuits embedded expressions.

## 3. Buffer and Window safety

The common buffer state contains an address, capacity, committed length and validated length, with `0 <= validated <= length <= capacity <= MaxObjectSize`. Committed bytes are initialized; the prefix ending at `validated` is valid UTF-8. Public Slices include only committed bytes. A Window exclusively borrows both the state and its uncommitted suffix; it writes contiguously from the start, without holes or uninitialized reads.

### 3.1. Reservation and growth

A successful `reserve(minimum)` leaves committed bytes and length unchanged and returns `written == 0` and `remaining >= minimum`. Zero permits an empty Window and causes no standard-buffer allocation by itself. Negative values Abort with `KIMI_E_ARG_RANGE`. `BufferFull` leaves committed bytes and length unchanged.

FixedBuffer succeeds exactly when the unused capacity is sufficient. HeapBuffer grows as follows; allocation failures are never translated to `BufferFull`:

1. If `minimum > MaxObjectSize - length`, Abort with `KIMI_E_ALLOC_SIZE`; otherwise set `required = length + minimum`.
2. If `required <= capacity`, do not grow, regardless of any hint.
3. On growth, set `target = required + hint`. If the hint is unrepresentable or exceeds `MaxObjectSize - required`, discard it and use `target = required`. Set the new capacity to `max(target, 2 * capacity)` when `capacity <= MaxObjectSize / 2`, or to `target` otherwise. Copy only committed bytes.

Only internal adapters provide a hint (§5.1).

### 3.2. Window operations

| Operation | Receiver | Behavior |
| --- | --- | --- |
| `written`, `remaining` | getter, `ref/Self` | Written bytes and remaining capacity. |
| `push(byte: u8)` | `uniq/Self` | `Result<(), BufferFull>`; appends one byte. |
| `append(bytes: Slice<u8>)` | `uniq/Self` | `Result<(), BufferFull>`; copies the entire Slice. |
| `limit(maximum: isize)` | `owner/Self` | Consumes and returns Self with capacity `min(capacity, maximum)`. `maximum < written` Aborts with `KIMI_E_ARG_RANGE`. |
| `commit()` | `owner/Self` | Commits the written prefix and returns its length as `isize`, without callbacks. |

`push` and `append` either fit completely or return `BufferFull` without changes. `limit` does not enlarge, duplicate or commit. Dropping an uncommitted Window makes no state change. There is no `advance(n)` or public construction from raw memory. Consumed Windows cannot be used again.

User Writers wrap a standard buffer or its borrow and forward or limit its Windows:

```kimi
struct Limited
    Self is BufferWriter
    var inner: Text.HeapBuffer
    let maximum: isize

    public init(maximum: isize)
        if maximum < 0 => $abort("maximum must be nonnegative")
        self.inner = Text.heap(0)
        self.maximum = maximum

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        let remaining = self.maximum - self.inner.length
        if minimum > remaining => return .Err(BufferFull.init())
        let window = try self.inner.reserve(minimum)
        return .Ok(window@move.limit(remaining))

struct LogWriter {source}
    Self is BufferWriter
    let target: uniq/Text.HeapBuffer during source

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        return self.target.reserve(minimum)
```

The adapter checks every user reservation in O(1): `written == 0` and `remaining >= minimum`. On violation it drops the Window without committing and records `BufferFull`. Standard Writers satisfy this by construction, so their checks may be omitted. Unverified bytes written by a user before returning a Window are never committed by the adapter.

### 3.3. Validation and destruction

Only a contiguous prefix known to be valid may be recorded as validated, using exclusive authority:

| Operation | Validation state |
| --- | --- |
| Raw `commit()` | Unchanged. |
| Adapter commit of valid encoded bytes | Advances to the new committed length only if `validated == length` at reservation. |
| `text()` | Scans only the unvalidated suffix; records nothing. |
| `validate()` | Scans the suffix; advances to committed length only on complete success, with no changes on failure. |
| `intoText()`, `intoString()` | Same suffix validation; expose a result only on success. |
| `clear()` | Resets validated length to zero. |

Appending only through an adapter to an empty or validated buffer needs no further validation. `validate()` does not reset adapter failure. Safe code, together with unsafe operations that satisfy their existing obligations, cannot expose uninitialized bytes or invalid UTF-8 as a string/view merely by violating formatting or Window laws. This does not constrain user side effects, termination or Abort.

FixedBuffer, WriteWindow and Utf8Writer have no `deinit` and do not observe their borrow at destruction. Their Loans may end after last use, unless a later use or `defer` keeps them live. HeapBuffer frees its allocation once. FixedBuffer never frees its source. General destructor rules are unchanged.

### 3.4. Optional in-place shrinking

After successful validation and before transferring ownership, `intoString()` may try shrinking excess capacity above an implementation-defined threshold (for example, at least one quarter of capacity and 64 bytes). It never tries for length zero. Shrinking preserves address, contents and sole release responsibility; capacity remains at least length. It cannot allocate, move or copy. Failure or lack of support leaves the allocation unchanged and still succeeds, without Abort, `InvalidUtf8` or retry. The same allocator must still release the pointer exactly once.

## 4. Formatting

`format` borrows its input and leaves no output dependency on that borrow or on the Writer. Successful output is the complete representation and must be the same for the same value and external state, whatever the destination Type or capacity. A Writer must report `BufferFull` instead of successfully abbreviating a value. This user law is not an optimization assumption. User formatting may have side effects or allocate; it runs once, without a size pre-pass or retry.

### 4.1. Failure and writes

Every adapter `write`, including those used by interpolation and Text helpers, follows these steps:

1. If already failed, return that failure without formatting or reserving, even for empty output.
2. Call the selected `format` once.
3. If it returns `BufferFull` or a nested write recorded one, retain and return the first failure. A user formatter cannot hide a failed write by returning success; its independently returned `BufferFull` is also retained.

A direct call to `format` is an ordinary call without this guard. A raw Window's failure does not update an adapter. Discarding a Result follows §17.4.

Built-in formatting checks status, computes the exact encoded byte length, reserves that amount once, appends and commits. Empty output makes no reservation. Each adapter commit is complete valid UTF-8; previously committed output survives failure. A three-byte representation such as `123` fits a three-byte destination exactly.

### 4.2. Default representations

| Value | Representation |
| --- | --- |
| Integer | ASCII decimal, `-` only for negatives, no redundant leading zeros; includes the minimum signed value. |
| `bool` | `true` or `false`. |
| `char` | UTF-8 encoding of the Unicode scalar. |
| Unit | `()`. |
| `string`, `Utf8Slice` | Their bytes, including NUL. |
| Floating point | The rules below, independent of locale. |

For a finite nonzero float, choose the decimal representation with the fewest significant digits that rounds to the original value in its original width using nearest-even rounding. Among equal-length candidates choose the closest to the exact value, then an even final significant digit to break a tie. `f32` uses its own rounding interval. Let `e` be the normalized decimal exponent: use fixed notation for `-4 <= e < 16`, scientific notation otherwise. Omit unnecessary fractional trailing zeros and decimal points. Use `.`, lowercase `e`, no exponent `+` and no leading exponent zeros. Special values are `0`, `-0`, `Infinity`, `-Infinity` and `NaN`; NaN sign and payload are ignored.

Borrow Types do not forward conformance; the argument adaptation of §5.2 selects the referent Type. Object handles, object borrows and pointers do not format implicitly, and diagnostics suggest an explicit payload dereference `@deref` (§13.5.5.1) where applicable. Tuples, arrays and user Types have no automatic conformance.

## 5. Interpolation and internal adapters

### 5.1. Capacity estimates

| Type | Maximum encoded bytes |
| --- | --- |
| `i8` / `u8` | 4 / 3 |
| `i16` / `u16` | 6 / 5 |
| `i32` / `u32` | 11 / 10 |
| `i64` / `u64` | 20 / 20 |
| `i128` / `u128` | 40 / 39 |
| `isize` / `usize` | Same as the pointer-width integer Type. |
| `bool` / `char` / Unit | 5 / 4 / 2 |
| `f32` / `f64` | 17 / 24 |

Strings, UTF-8 views and user Types are unbounded. Bounds and literal lengths are summed at compile time; exceeding MaxObjectSize discards an estimate rather than Aborting.

Interpolation and `Text.toString` create private HeapBuffers and internal adapters. A bounded interpolation (all values bounded, total at most MaxObjectSize) allocates its bound once and never grows; a zero bound starts unallocated. Other cases start unallocated. Neither expressions nor user formatters are evaluated early to obtain lengths.

Before each value, the outer lowering sets a hint to the sum of later literal lengths and bounded-value maxima, excluding the current value and counting unbounded values as zero. Every nested reservation on that internal adapter uses the current hint. Nested `$tryWrite` does not replace it.

Under §6, internal literal writes and allocation may be delayed until the next reservation. Include pending bytes in its actual required size and write them first. Flush remaining literals before successful completion even if subsequent values never reserve. Account for literal bytes and check actual size limits before evaluating the next expression; physical delay cannot defer those checks.

These rules follow adapter creation, not the spelling of a nested call. Internal adapters retain their hint/pending state through user `format`, `write` and `$tryWrite`. Adapters created by `Text.writer` have neither state and preserve observable reservation arguments and counts. User formatters cannot observe that internal state. Actual-size overflow Aborts; hint overflow merely discards the hint. No reevaluation or allocation retry is permitted.

### 5.2. Owning interpolation

The lexical syntax is §2.9. Each embedded expression fits `write<T>(value: ref/T)` under §10.2, with static `T is Utf8Format`:

| Expression | Adaptation | T |
| --- | --- | --- |
| Owned Place or temporary, including Copy values, whatever the access path | Shared borrow | Expression Type |
| `ref/U` | Exact | U |
| `uniq/U` | Shared Reborrow | U |

A bare expression does not Move its source, and `\(x@move)` borrows the transferred temporary. Literals use the normal default Types, without an expected `string` Type.

Lowering creates the internal adapter, then writes left to right. Evaluate each expression once and complete its write before evaluating the next, subject only to §5.1 and §6. The first failure Aborts with `KIMI_E_FORMAT: Formatting failed`. On success, end the adapter's uses and consume the HeapBuffer into an owning string. This buffer is always fully validated, so completion performs no scan and has no validation-failure branch. Value borrows end at the end of each write; the existing temporary destruction boundaries are unchanged.

```kimi
let message = "My number is \(self.number)"
```

### 5.3. Short-circuiting `$tryWrite`

`$tryWrite(writer, literal)` is a Composition Root operation, not a Function value. Its result is `Result<(), BufferFull>` and it creates no combined string.

The first operand is acquired as `uniq/Utf8Writer`. It is an operand, not a Receiver Expression (§7.3), so a bare owned Place is rejected, whatever its access path, with a suggestion to use `@uniq`, while a borrow value is Reborrowed normally. The second operand must syntactically be an ordinary or raw string literal, with or without substitutions; use `write` for an arbitrary string value.

Evaluate the first operand once and activate its exclusive borrow immediately, before any embedded expression; call borrow reservations (§15.6.7) do not apply. Write segments and values left to right using the adaptation of §5.2. At the first failure, stop without evaluating later expressions; an adapter that has already failed at entry skips all expressions. Every expression's Types, conformance and control-transfer targets are still checked statically. Embedded expressions cannot read the borrowed adapter or its source. Preserve the original temporary scopes and the targets of `return`, `exit` and `yield`. Drop uncommitted Windows and keep committed output.

```kimi
struct Point
    Self is Utf8Format
    var x: i32
    var y: i32

    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
        return $tryWrite(writer, "(\(self.x), \(self.y))")
```

Passing an interpolated literal directly to `Utf8Writer.write` first completes ordinary owning interpolation. Warn and suggest `$tryWrite` without changing the meaning; this warning is independent of the Result-discard diagnostic priority (§17.4).

## 6. Optimization and output

Direct encoding, reservation coalescing, delayed allocation, stack placement and fill-store elimination preserve accepted programs, selected implementations, Loans, Origins, temporary lifetimes, evaluation order/count, user effects, destruction and control transfers. Coalescing and direct encoding require a known standard Writer.

Preserve `BufferFull`, explicit Aborts, argument checks and actual-size checks, including their order relative to earlier effects. Only private interpolation/`toString` buffer allocation and release may have resource-failure timing or occurrence changed by elision, coalescing, delay or stack placement. This exception never covers user allocations/frees, user reservations or OS output. Failures of operations actually performed still use existing Abort and source-position rules. Fill stores may be omitted only when the array is read exclusively through FixedBuffer for its entire lifetime; evaluating/acquiring the fill value still follows §4.3.

### 6.1. Console output

`Kimi.Console` provides `writeLine(text: ref/string) -> ()` and `writeLine(text: Text.Utf8Slice) -> ()`. Each writes all UTF-8 bytes and LF via `WriteStdout(data, length)`, borrowing rather than retaining or releasing the input. No string handle need be constructed for a view. Output errors, possible partial output and flushing follow §22.4. Ordinary overload resolution selects only the string-borrow candidate for strings and the exact view candidate for views; there is no implicit conversion. A function reference without an expected Type is ambiguous.

For a directly passed, bounded interpolated literal within an implementation-defined stack limit, lowering may use a fixed stack region of exactly the estimated size. It needs no fill or UTF-8 scan, and cannot overflow, fall back to a heap path or repeat evaluation. Complete all formatting before output; formatting failure emits no body. Expression Aborts/control transfers and subsequent OS partial-output errors retain their normal behavior. Larger or unbounded interpolation uses the owning path.

The Windows x64 profile uses a 1,024-byte limit, including all literal bytes and value maxima. The same rule applies after generic substitution. Stack storage has no release operation, including when an embedded expression leaves the enclosing function or control-flow construct.

```kimi
func printNumber(n: i64) -> Result<(), BufferFull>
    var scratch: [64 of u8] = [64 of 0]
    var buffer = Text.fixed(scratch@uniq)
    var writer = Text.writer(buffer@uniq)
    try $tryWrite(writer@uniq, "My number is \(n)")
    match buffer.text()
        .Ok(let text) => Console.writeLine(text)
        .Err(_) => $abort("unexpected invalid UTF-8")
    return .Ok(())
```

### 6.2. Required costs

| Path | Requirement |
| --- | --- |
| Built-in formatting into a fitting FixedBuffer | No heap allocation, intermediate string, boxing or argument array. |
| Built-in formatting into HeapBuffer within capacity | No additional allocation; capacity can be reused after `clear()`. |
| `text`, `validate`, `intoText`, `intoString` with `validated == length` | Zero scanned bytes; no additional allocation or copy. |
| Owning bounded interpolation | At most one heap allocation; no completion copy. |
| Interpolation with exactly one unbounded string/view, preceded only by literals | At most one allocation (zero for empty output), unless its hint was discarded. Other values must be bounded. |
| `Text.toString(s)` for a string | Empty: zero allocations, Static result. Nonempty: one allocation of exactly its byte length. |
| Optimized `Console.writeLine("My number is \(n)")`, `n: i64` | A 33-byte stack region (13 literal + 20 numeric), no heap allocation. |

The initial generic profile remains monomorphization (§21.3.1). Built-in formatting uses direct calls or inlining. Inlining/automatic specialization may duplicate code subject to §6, without requiring Writer-based format instantiations. Arbitrary owned results, growth beyond capacity, user implementations and OS internals have no general zero-allocation guarantee.

## 7. Conformance verification

Native execution, allocation counters and generated-code inspection must cover:

- All default representations, numeric extremes and byte maxima, original-width float round trips, UTF-8 rejection cases, NUL and empty output; exact-fit success and one-byte-short failure.
- Once-only expression/format evaluation, ignored write failures, independently returned BufferFull, normal-call argument evaluation versus `$tryWrite` short-circuiting, and original cleanup/control-transfer boundaries.
- Actual-size overflow versus discarded hints, growth arithmetic, no hint-only growth, zero reservations, nested writes retaining outer hints, pending literals including following empty values, and observable user reservation arguments/counts.
- Invalid returned Windows (prewritten or too short), atomic append failure, limits, drop without commit, rejection of double commit/use after consumption and conflicting aliases/reservations/growth.
- Validation of only contiguous prefixes, no recording by `text`, success-only recording by `validate`, no skipping invalid prefixes, clear/reuse and sticky failure.
- Retention of exclusive source Loans by shared results, caller-origin escape versus local-source rejection, erased-target lifetime, last-use reuse versus later uses/defer, and equivalent explicit/elided Origins.
- Transitive effect-bound rejection, including unknown calls, lazy initialization and destruction; ordinary Loan checking for borrowed static storage reached through `self`.
- Required explicit acquisition, non-consuming substitutions, fixed fill including length zero and rejected Array expectations, borrowed Console inputs, absence of output before successful formatting, and exactly-once cleanup on normal/early exits.
- All required costs, stack-limit fallback without reevaluation, absence of Writer-driven format instantiation, and optional shrink threshold/zero/failure/unsupported paths without allocation, Abort or ownership changes.
