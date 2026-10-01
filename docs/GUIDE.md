# Kimigayo Coding Guide

Non-normative, compressed excerpts from [SPEC.md](SPEC.md), with conventions from [STYLE.md](STYLE.md).
SPEC and its chapters control language meaning; STYLE controls conventions only. This guide adds no rules.
Examples are independent fragments. Follow the links for omitted details. Check [LIBRARY.md](LIBRARY.md) for APIs
and [STATUS.md](STATUS.md) for compiler support. A specification rule is not a claim of implementation.

## 1. Source and programs

[Source §2](spec/02-source-and-lexical-structure.md), [modules §18](spec/18-modules-and-dependencies.md),
[startup §22.2](spec/22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization).

- UTF-8; case-sensitive NFC names. Four spaces per indent; no indentation tabs. Newlines separate items.
- Bodies use an indented block or `=> expression` on the header's ending line. No body colon or braces.
  Use delimiters for multiline expressions. Executable bodies cannot be empty: write `()` for no work.
- `//` comments; non-nesting `/* ... */`; `///` documentation. Strings use `"text \(expression)"`,
  chars use `'A'`; `"""raw text"""` has no escapes or interpolation.
- Choose top-level executable items in exactly one file **or** root-level `public func main() -> ()`.
  Libraries have neither executable top-level items nor automatic startup.
- Kimi's direct members are open by default: `Array`, `Option`, `Console`, etc.; use `Console.writeLine(text)`.
  At the file start, `alias A.B` opens direct members; `alias B => A.B` names a module/group qualifier.
  Aliases are file-local; external modules must be direct project dependencies. `::` starts at the compilation root.
- Top-level variables and ordinary functions are file-local. Put shared functions/data in a `group`.
  Access defaults to `private`; `internal` means this module; `public` still requires accessible enclosing containers.

```kimi
public func main()
    Console.writeLine("Hello, world!")
```

## 2. Values and expressions

[Types §3](spec/03-types-and-values.md), [expressions §12](spec/12-expressions.md),
[operators §13](spec/13-operators-and-assignment.md).

- `let x: T = value` binds once; `var x: T = value` permits reassignment. An omitted initializer leaves storage
  uninitialized, never zero-filled. An omitted Type needs an initializer. Parameters are immutable bindings.
- Integers: `i8/i16/i32/i64/i128/isize`, `u8/u16/u32/u64/u128/usize`; floats: `f32/f64`; also `bool`, `char`,
  UTF-8 `string`, Unit `()`. Unconstrained integer/float literals default to `i32`/`f64`.
- Established numeric Types never mix implicitly. Convert with `x@i64`; integer range checks and invalid
  float-to-integer conversions Abort. Integer arithmetic is checked; division truncates toward zero.
- `Wrapping<u32>` is a distinct integer Type whose arithmetic wraps modulo 2ᴺ instead of Aborting (division
  by zero still Aborts); enter and leave it with `x@Wrapping<u32>` and `w@u32`. `x@wrap<u8>` wraps any
  integer to another width or signedness, and `f@bits<u32>` reinterprets a float's bits; neither fails.
- Boolean operators are short-circuit `and`, `or`, and unary `not`; conditions require `bool`.
  Write `not (a == b)` and `a < b and b < c`. Bitwise operators: `& | ^ << >>`.
- Tuples: `(a, b)`, `(a,)`, fields `.0`, `.1`; Types `(A, B)`, `(A,)`. `(x)` is grouping.
- `T?` means `Option<T>`; `ref/T?` means `Option<ref/T>`, while `ref/(T?)` borrows an Option.
  Construct `.Some(value)` / `.None` explicitly. `null` is only for raw pointers.
- Evaluate operands and explicit arguments left to right; omitted defaults run afterwards in parameter order.
  Assignment evaluates its target before its source and returns Unit. Parenthesize adapted values before
  member access: `(x@T).member`; `@move`, `@copy`, borrow shorthand and `@follow` can chain directly.

## 3. Ownership: choose the operation

[Acquisition §15.1](spec/15-ownership-and-lifetime-analysis.md#151-initialization-and-consume-analysis),
[implicit adaptation §10.2](spec/10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types).

A **Place** is storage, such as a local or an element. `T` (= `owner/T`) owns a value;
`ref/T` shares read access; `uniq/T` grants exclusive mutable access. Type prefixes add layers.

| Need | Parameter | Call with an owned local `x` |
| --- | --- | --- |
| Read a small Copy value | `T` | `f(x)` |
| Inspect without ownership | `ref/T` | `f(x)` (implicit shared borrow) |
| Mutate | `uniq/T` | `f(x@uniq)` (`x` must permit mutation) |
| Store, return or consume | `T` | `f(x@move)`; bare `x` is allowed only if Copy |

- Bare acquisition of a Place copies only a proven Copy Type; it **never moves**. `@move` transfers and
  invalidates the source even for Copy Types. Temporaries transfer as values without spelling `@move`.
- Scalars, Unit, positions and ranges, shared references, raw pointers and Slice handles are Copy. Tuples/fixed arrays are Copy
  when all parts are. User structs/enums opt in with `Self is Copy`; all storage must qualify and no user
  `drop` is allowed. `string`, `Array`, `Dictionary`, exclusive references and owning object handles are Non-Copy.
- `x@ref` / `x@uniq` borrow the **written slot**. `r@follow` selects one safe reference's referent.
  Thus `r@ref` adds a layer; `r@follow@ref` borrows its target; `r@follow += 1` updates its target.
  `r@move` transfers the reference, never the referent. `r@follow@copy` copies a proven-Copy referent.
- A fixed reference expectation copies/reborrows existing references as needed; passing `r: uniq/T` to
  `uniq/T` or `ref/T` needs no `@uniq`. Safe reference chains can supply one shared reference or a value read.
  With no expectation, `let x = sharedRef` copies the reference, not its target, and `let x = uniqRef`
  reborrows it exactly as `let x: uniq/T = uniqRef` does; `uniqRef` is usable again after `x`'s last use.
- New exclusive borrows are explicit except for method/accessor/callable receivers: `values.append(item)`
  acquires its mutable receiver implicitly. `let`-owned storage cannot be lent exclusively; a `uniq/T` held
  in a `let` or parameter still permits referent mutation. No implicit borrowing of a reference/handle slot.
- Borrows cannot outlive their source or conflict with mutation, Move or destruction. Temporaries normally
  end with the full expression: retain an owner before saving its view. Copying a view extends no lifetime.
- Cannot Move out through a borrow or from dynamic collection indexing; use removal or consuming iteration.
  Partial Moves require trackable owned paths and obey `drop` restrictions. A Moved `let` cannot be restored.

## 4. Functions and control flow

[Functions §7](spec/07-functions-and-callable-values.md), [control flow §14](spec/14-control-flow.md).

```kimi
func scaled(value: i32 ! by => factor: i32 = 2) -> i32 => value * factor
func doubled(value: i32) -> i32
    return scaled(value, by: 2)

let sign = if doubled(3) > 0 => 1 else => -1
let answer = do => 42
```

- Named functions without `-> T` return Unit. `=>` supplies an expression result unless Unit is fixed.
  Indented bodies discard **every** direct expression, including the last: use `return` for a function,
  `yield` for a value-producing `if`/`match`, or `exit to target value` for a labeled `do`.
- `!` replaces the comma before required-name parameters. Before it, arguments may be positional or named;
  after it, names are required. `external => internal` renames a parameter. Defaults control omission separately.
- `while condition`, `for item in source`, `loop` repeat; `exit` leaves a loop, `continue` advances it.
  `for`/`while` return Unit; a value-used `loop` gets its result from `exit value`.
  Labels: `label outer: loop`, `exit to outer`, `continue to outer`, `yield to choice value`. The introducer `label` is contextual; named transfer operands have no colon.
  Unnamed `yield` targets the nearest `if`/`match`, even inside another selection; label outer targets.
- `match` must be exhaustive. Patterns: `_`, literals, `let x` / `var x`, tuples, enum Cases; guards use `if`.
  There is no `if let`, bare-name binding Pattern or `case` keyword in runtime arms.
- `match x` / `for item in x` borrow a bare Place in place. For owned sources, `x@uniq` gives exclusive
  access; `x@move`, `x@copy` or a temporary gives by-value acquisition. A transferred reference keeps its mode.
  Match payload bindings are shared/exclusive references or transferred values according to the path.
  Guards inspect candidates through shared references before the selected arm acquires them.

```kimi
func orZero(value: Option<i32>) -> i32
    return match value
        .Some(let number) => number // Shared payload; read as the expected i32.
        .None => 0

var numbers = [1, 2, 3]
for number in numbers@uniq
    number@follow += 1
```

## 5. Types, members and generics

[Declarations §6](spec/06-declarations-and-containers.md), [contracts §8](spec/08-generics-constraints-and-contracts.md),
[Properties §11](spec/11-properties.md).

```kimi
struct Counter
    var count: i32 = 0

    public computed isEmpty: bool
        get() -> bool => self.count == 0

    public func advance(self: uniq/Self) => self.count += 1

enum Outcome<T>
    Value(T)
    Missing

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

- Construct structs with `Type.init(args)`, never `Type(args)` or field braces. Declare `public init(...)`
  and initialize every field. An implicit public `init()` exists only without explicit constructors,
  with all own fields initialized and an accessible zero-argument base invocation; no memberwise synthesis.
- Bare `self` means `self: ref/Self`; `self: uniq/Self` mutates; `self: Self` consumes/copies.
  No `self` means a Type function. Same-name receiver overloads must share one receiver shape.
  Use separate shared/exclusive names. Groups contain static members; there is no `static` modifier.
- Stored Properties use `let`/`var`, optionally `get` and narrowed `private set`.
  `computed p: T` requires `get() -> T`; optional `set(value: U) -> ()` may take a different Type.
  Omitted instance accessor receivers are shared for get, exclusive for set. Custom getters return values.
- Enums declare `Name` or `Name(T, U)` Cases. Construct `Outcome<i32>.Value(1)` or `.Value(1)` with a known
  expected enum Type. Payload-free Cases have no parentheses. Enums have no stored/computed Properties.
- `<T>` binds a complete Type, including references. `<s/T>` binds one argument, split into Semantics `s`
  and target `T`; constrain `s` before using target-specific operations. `<length N>` is function-only.
- Put constraints first in the body: `T is Copy`, `T is Equatable and Owned`. Generic bodies must be valid
  for all admitted Types. `Owned` means no non-static safe-borrow dependencies, not merely owning Semantics.
- `contract C` declares signatures, `property p: T has get, set`, and `associate Item`.
  A struct/enum writes `Self is C`, supplies accessible implementations and `associate C.Item is T`.
  Project as `T.(C).Item` (`T.Item` when unique). Conformance and associated Types are explicit, not duck-typed.
  Conditional conformance: `Self is Copy when T is Copy`. Contract refinement: `contract C: A, B`.
- Supply all explicit generic arguments or omit the list. Use annotations/explicit arguments when inference
  is ambiguous; later uses and callee bodies provide no evidence. Constructors require bound Type arguments.
- `place ref/T` / `place uniq/T` function results publish existing storage: return the Place directly.
  Callers may read Copy values or borrow it, and replace through the exclusive form; never Move out.

## 6. Borrowed results and stored views

[Origins §15.3–4](spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations).

```kimi
func first<T>(values: ref/Array<T>) -> ref/T during values => values[0]@ref
func tail<T>(values: Slice<T>) -> Slice<T> during values.source => values[1..]

struct View<T> {source}
    public let value: ref/T during source

    public init(value: ref/T during source)
        self.value = value
```

- `during source` states a borrow dependency; nested layers keep separate Origins. `during (a and b)` means
  the intersection. Input names can identify their direct borrow Origins; `value.source` projects a schema slot.
- Declare stored Origins explicitly, e.g. `{source}`; bind every stored borrow/aggregate slot. Initializers
  do not infer the storage contract. `View<T>{v}` names that occurrence's binding set; use attached
  `origin v.source == source` or `origin a outlives b` (a lasts at least as long as b).
  A one-slot Type accepts `Slice<T> during source`. There are no function Origin parameter lists.
- Omitted result Origins use the intersection of **all direct borrowed inputs**, including the receiver.
  Aggregate-internal Origins are not candidates. With no such input, shared results default to `static`;
  exclusive results require an explicit contract. Aggregate defaults additionally require Owned inputs and
  a non-exclusive slot. State the actual source explicitly, especially for borrowed aggregate results.

## 7. Collections

[Arrays, indexing and slices §4](spec/04-arrays-indexing-and-slices.md),
[iteration §22.1.2](spec/22-core-execution-and-foreign-functions.md#2212-iteration-and-storage).

- `[1, 2]` normally creates `Array<i32>`; `let a: [2 of i32] = [1, 2]` creates inline fixed storage.
  `[N of value]` always creates a fixed array, evaluates value once, and requires Copy elements.
  Empty collections need element Types. No implicit fixed-array/Array/Slice conversions.
- Positions are integers of any Type, `^x` (`FromEnd<T>`, counted from the end) and omitted `Start`/`End`.
  `^1` is last; `^0` is the end boundary, invalid as an element. Invalid indexing Aborts; `tryGet` returns
  an optional reference and `trySlice` an optional Slice. Constructing `^x` or a range checks nothing.
- `a..b` (`Range<S, E>`) excludes b; `a..=b` (`ClosedRange<S, E>`) includes b; omitted ends are `Start`/`End`.
  Only ranges whose two boundaries share one integer Type iterate: `0..n`, `values.indices` or
  `r.resolve(values.length)`. Reversed iteration ranges Abort.
- `values[range]` / `values[..]` returns a shared `Slice<T>` retaining the backing Loan. A `var` Slice or
  `uniq/Slice<T>` does not make elements mutable. Even an empty array-derived Slice can block source mutation.
- Array/fixed-array iteration yields `ref/T`, `uniq/T`, or `T` for shared, exclusive, or consuming access.
  Slice iteration always yields shared elements. `for var x` changes the local binding, not referent access.
- Dictionaries use `[key: value]` or typed `[:]`; keys require stable Equatable equality; duplicates are errors.
  Iteration is insertion-ordered;
  `for (key, value) in map@uniq` gives a shared key and exclusive value. Indexed assignment replaces an
  existing entry; use `insert`/`tryInsert` to add one. Check LIBRARY for exact signatures and outcomes.

## 8. Failure and cleanup

[Failure §17](spec/17-failure-handling.md), [cleanup §16](spec/16-scope-exit-and-destruction.md).

- Use `Option<T>` (`Some`, `None`) for absence; `Result<T,E>` (`Ok`, `Err`) for recoverable reasons.
  `try expression` extracts one owned Option/Result layer; failure returns from the nearest function.
  That function must return the same family, with a compatible error Type for Result.
- `try pending@move` consumes a stored Non-Copy result. Borrowed enums are not automatically unwrapped.
  Wrap success explicitly: `return .Ok(value)`. `try` gives no inference context to its operand.
  No exceptions, `catch`, postfix `?`, implicit error conversion or implicit Some/Ok wrapping.
- `require condition else => return ...` guards continuation; the else body must not continue normally.
  For violated preconditions: `require condition else => $abort("Invalid count")`.
  Abort terminates without guaranteed cleanup; recoverable failure uses ordinary returns.
- `_ = expression` intentionally acquires and discards a value; ignored Result otherwise warns.
- `defer => cleanup()` runs on normal scope exit and transfers. Defers and remaining owned locals clean up
  in reverse combined declaration order. `drop` adds struct cleanup; automatic field destruction follows.
- Tests use `#Test` on eligible parameterless Unit functions; `$expect(condition)` records a failure and
  continues; `$require(condition)` Aborts the test case without unwinding. Both statements are test-only.

## 9. Callbacks and less common features

- Closure: `func [captures] (x: T) -> U => expression`. Without a fixed expected signature, annotate inputs.
  Omitted captures are Copy-only; `[]` forbids captures. An explicit entry `x`, `x@move`, `x@ref` or `x@uniq`,
  optionally with `var`, initializes the environment binding as `let x = x` or `let x = x@op` would, so `x@ref`
  borrows the outer slot. Captures execute at creation. Named nested functions cannot capture.
- Prefer generic callbacks: `callback: ref/F` with `F is Callable<(T) -> U>`; use `uniq/F` and
  `Callable<uniq, (T) -> U>` for mutation, `F` and `Callable<owner, (T) -> U>` for consumption.
  Non-Copy consuming closures need `callback@move()`. Erased `(T) -> U` values are Non-Copy, Shared-callable,
  and require an Owned environment. See [§7.6](spec/07-functions-and-callable-values.md#76-function-expressions).
- Objects: `obj/T` owns exclusively; `rc/T` / `arc/T` share ownership; `objref/T` / `objuniq/T` borrow objects.
  Create with `makeObj`, `makeRc` or `makeArc` in `Kimi.Intrinsics`, moving Non-Copy inputs explicitly;
  duplicate counted handles with `Kimi.Intrinsics.clone(handle@ref)`. Bare `@obj`/`@rc`/`@arc` are invalid.
  See [§13.5](spec/13-operators-and-assignment.md#135-explicit-operations) before using views or Weak.
- Raw pointers are `unsafe/T`; dereference `*p` only in `unsafe` code. Safe references use `@follow`.
  See [§5](spec/05-raw-pointers-and-unsafe-memory.md) for validity and conversion obligations.
- Target selection uses `#if condition` or `#switch` with indented `#case condition` / `#case _` arms;
  conditions read compilation symbols, not runtime locals. See [§19](spec/19-compile-time-directives.md).
- Consult SPEC before inheritance, specialization, custom indexing/iteration, FFI or buffer formatting.
  Do not invent extensions, virtual methods, runtime Contract casts, operator overloads or string `+`
  ownership rules. These boundaries are indexed in [Appendix D](spec/appendices/D-deferred-features.md).

## 10. Style defaults

[STYLE.md](STYLE.md) is mandatory where tagged `[Kimi]` for the Kimi library and specification examples;
elsewhere its conventions are recommended.

- UpperCamelCase Types, Contracts, groups, Cases and files; lowerCamelCase functions, Properties and locals.
  Type parameters `T`, `Key`; length `N`; Semantics `s`; Origins `source`. Acronyms are words: `Utf8Writer`.
- Keep lines within 120 columns; short bodies use `=>`; separate declarations with one blank line.
  Order: constraints, associated Types, Cases, stored/computed Properties, constructors/drop, functions,
  nested Types. Preserve storage order; keep overloads adjacent, shared variants first.
- Use `sorted`/`sort`, shared/exclusive `tryGet`/`tryGetUniq`, conversion `toString`, Boolean `isEmpty`.
  Use required argument names for ambiguous inputs. Defaults should be cheap and free of observable effects.
- Properties are stable O(1) observations without allocation/effects; work belongs in functions.
  Borrow large inputs, pass small Copy values/Slice handles by value, consume only when needed.
  Compare through `Equatable.equals` / `Comparable.compare` (negative/zero/positive `i32`).
- Prefer typed errors to strings or sentinel values; return rejected owned inputs when recovery needs them.
  Use `try` names for recoverable failure unless the name already expresses checking; normal absence needs none.
- Document public APIs with `///` before Attributes: summary, then ownership, effects, cost and applicable
  `- name:`, `- return:`, `- abort:`, `- safety:` items. Use short capitalized Abort messages without a final period.
