# Kimigayo Style

Coding conventions for Kimigayo source. [SPEC.md](SPEC.md) takes precedence. Style cleanup must preserve
specified API and Contract signatures.

## 0. Scope and rule levels

| Tag | Meaning | Kimi library and specification examples | Other Kimigayo code |
| --- | --- | --- | --- |
| `[Language]` | A language requirement, summarized for context | Required by SPEC | Required by SPEC |
| `[Kimi]` | A library coding convention | Required | Recommended |
| `[Advice]` | A recommendation | Explain deviations when reviewing changes | Recommended |

The Kimi scope is `Kimi/Library` and examples in `spec/`; other code includes user programs, `examples/`,
milestones and tests. A tag covers its paragraph and any table, list or example it introduces.

Apply conventions to changed declarations, without unrelated formatting or behavior changes. Examples
may omit context, bodies and documentation, or use explicit syntax for teaching; they need not be complete
programs. Hypothetical APIs do not imply support; see [STATUS.md](STATUS.md).

## 1. Naming

### 1.1. Names and files

`[Kimi]` Use these forms:

| Declaration | Form | Examples |
| --- | --- | --- |
| struct, enum, group | UpperCamelCase noun; a group names its domain | `ResolvedRange`, `Option`, `Text` |
| Contract | UpperCamelCase capability adjective or role noun | `Equatable`, `Indexable`, `Iterator`, `BufferWriter` |
| enum Case, associated Type | UpperCamelCase noun or adjective | `Some`, `None`, `Item` |
| Type parameter | One capital letter, or an UpperCamelCase role | `T`, `E`, `K`, `V`, `F`, `Key` |
| length parameter | One capital letter | `N`, `M` |
| Semantics parameter | One lowercase letter | `s` |
| Origin | lowerCamelCase noun naming the borrowed source | `source`, `target` |
| function | lowerCamelCase operation name; see the variants below | `append`, `validateUtf8`, `toString` |
| Property | lowerCamelCase noun; Boolean names use `is`, `has` or `can` | `length`, `capacity`, `isEmpty` |
| parameter, local | lowerCamelCase name describing its role | `index`, `destination` |
| file | UpperCamelCase `.kimi`, named for its main declaration or topic | `Array.kimi`, `ArrayOperations.kimi` |

- `[Language]` Names are case-sensitive and must be in NFC; `_` alone is not a Name
  ([SPEC §2.5](spec/02-source-and-lexical-structure.md#25-names)).
- `[Kimi]` Treat acronyms as words: `Utf8Writer`, not `UTF8Writer`. Use familiar abbreviations such as
  `min`, `max` and `utf8`; prefer `index` to `idx`.
- `[Kimi]` Put helpers in their domain's group or struct, not a catch-all `Utils` or `Helpers` group.
- `[Advice]` Put one primary public declaration in each file. Split large Types by topic, preserving
  storage order (§2.2).

### 1.2. Related operations

`[Kimi]` Follow these naming patterns, including the pairs in
[SPEC §4.7.1](spec/04-arrays-indexing-and-slices.md#471-common-acquisition-and-outcomes):

| Relationship | Pattern | Examples |
| --- | --- | --- |
| New value / in-place change | Past participle / verb | `sorted` / `sort`, `reversed` / `reverse` |
| Shared / exclusive borrowing variants | Add `Uniq` to the exclusive variant | `tryGet` / `tryGetUniq`, `index` / `indexUniq` |
| Inspect / advance or take | Different verbs | `peek` / `next` |
| Conversion to another value Type | `toX` | `toString` |
| Borrowed view construction | A noun naming the view | `utf8` |

`[Kimi]` `Uniq` identifies exclusive receiver access; the signature determines result capabilities.
For example, `iterateUniq` need not yield exclusive references. Failure-related names follow §5.2.

## 2. Layout and organization

### 2.1. Formatting

- `[Language]` Each indentation level is four spaces; tabs are forbidden in indentation. A body is
  `=> item` on the header's ending line or an indented block
  ([SPEC §2.2](spec/02-source-and-lexical-structure.md#22-lines-indentation-and-continuation)).
- `[Kimi]` Use `=>` for a short, single-item body that fits on one line. Otherwise use a block.
- `[Kimi]` Keep source lines within 120 columns.
- `[Kimi]` Write ordinary comments as `// ` followed by a sentence explaining intent or a non-obvious
  constraint. Cite rules as `SPEC 4.7.1`.

### 2.2. Declaration order and spacing

`[Kimi]` Where the container permits them, arrange declarations in this order:

1. Constraints and conformances.
2. Associated Types.
3. enum Cases.
4. Stored Properties, then computed Properties or Contract Property requirements.
5. Constructors, then `deinit`.
6. Functions grouped by operation, with public API groups before helper-only groups. Keep overloads adjacent
   and shared variants before exclusive variants.
7. Nested Types.

`[Kimi]` Preserve Field order and layout contracts. Do not regroup `let` and `var` Fields or move them
across fragments for style: logical order controls initializer effects and reverse destruction
([SPEC §6.2.1](spec/06-declarations-and-containers.md#621-split-structures-and-storage-order),
[§16.3.2](spec/16-scope-exit-and-destruction.md#1632-field-cleanup)).

- `[Kimi]` Separate declaration groups with one blank line. Use one blank line between constructors,
  `deinit`, functions, computed Properties with bodies, and top-level declarations. Related bodyless
  signatures or simple stored declarations may stay consecutive.
- `[Advice]` Inside a body, separate logical steps with a blank line.

## 3. API design

### 3.1. Receivers

- `[Language]` Within one function group, all functions that have a receiver share one receiver shape.
  Receiverless functions do not participate in this rule
  ([SPEC §7.3](spec/07-functions-and-callable-values.md#73-explicit-receivers)).
- `[Kimi]` Use bare `self` for read-only methods and `self: uniq/Self` for mutation. Write the full Type
  when stating an Origin or explaining a language rule.
- `[Kimi]` Use `self: Self` when consuming the value or passing a small Copy value such as a Slice handle.
  Use a Type function, without `self`, for an operation that needs no existing instance.

### 3.2. Parameters and overloads

- `[Kimi]` Name parameters by role. Leave an argument positional when the operation explains it:
  `values.append(item)`, `values.remove(2)`.
- `[Kimi]` Use `!` to require names where positions are ambiguous, especially for Booleans, counts,
  capacities or two similar inputs: `init(! start: isize, end: isize)`.
- `[Advice]` Use `external => internal` when it improves the call:
  `of => value: ref/T` permits `firstIndex(of: target)`.
- `[Kimi]` Overloads describe the same operation with different input forms, such as `isize` and `Index`.
- `[Kimi]` Choose defaults that are common, cheap, free of observable effects and require no allocation.

### 3.3. Properties and comparisons

- `[Kimi]` Use a Property for a stable observation with no explicit arguments, allocation or observable
  effects, and O(1) cost: `length`, `capacity`, `isEmpty`. Use functions for work such as `sorted()`.
- `[Advice]` Prefer shared getters. An operation that needs exclusive access or consumes its receiver
  is usually clearer as a function.
- `[Kimi]` Use `Equatable.equals` and `Comparable.compare` for equality and ordering. The latter returns
  a negative, zero or positive `i32`. Do not introduce competing comparison Contracts or Boolean
  `lessThan` callbacks for the same purpose.

## 4. Ownership and borrowing

### 4.1. Inputs

`[Kimi]` Choose input acquisition by what the operation needs:

| Need | Parameter | Caller with an owned Place |
| --- | --- | --- |
| Read a small Copy value | `T` | `f(value)` |
| Read other values | `ref/T` | `f(value)` |
| Mutate the caller's value | `uniq/T` | `f(value@uniq)` |
| Store, return or consume a value | `T` | `f(value@move)`; a Copy value may be passed bare |

`[Kimi]` Pass integers, `bool`, `Index`, `Range` and `Slice` by value; borrow large Copy aggregates.
Do not consume a Non-Copy input merely to inspect it. Temporaries and existing references follow ordinary
acquisition and Reborrow rules.

`[Advice]` Prefer ownership and borrows to `rc`/`arc` unless sharing is part of the design. Keep borrows
short; save independent observations such as `length` before mutation when that preserves the algorithm.

### 4.2. Results and element access

`[Kimi]` Distinguish these operations:

| Intent | Result |
| --- | --- |
| Index an existing element | `place ref/T` or `place uniq/T`, following Indexable |
| Find an element that may be absent | `Option<ref/T>` or `Option<uniq/T>` |
| Copy a snapshot | `T`; state Copy constraints or explicit copying costs |
| Remove or otherwise transfer an element | `T` or `Option<T>`, with the transfer documented |

`[Kimi]` Keep a lookup's reference result independent of the element's Copy capability, as in `tryGet`.
A Place result exposes storage; its caller may borrow it or read a Copy value. It is not a reference value
([SPEC §4.6.9](spec/04-arrays-indexing-and-slices.md#469-indexable-contracts)).

`[Kimi]` State result Origins where permitted, including borrows inside aggregates: `during self` for an
owning collection, `during self.source` for a Slice. Use the actual source; a newly returned value or a
copied snapshot may still retain dependencies.

### 4.3. Callbacks

`[Kimi]` Use generic `F is Callable<...>` by default for public callbacks. Borrow callbacks used only
during the call; take ownership when storing, returning or consuming them.

| Intent | Parameter | Constraint |
| --- | --- | --- |
| Shared invocation | `callback: ref/F` | `F is Callable<S>` |
| Allow callback state to change | `callback: uniq/F` | `F is Callable<uniq, S>` |
| Consume the callback on invocation | `callback: F` | `F is Callable<owner, S>` |

`S` stands for a signature such as `(ref/T) -> bool`. The exclusive form also accepts Shared-callable
values that can be lent exclusively. Stored callbacks use the constraint needed for later calls.

`[Language]` Concrete Callable use creates no erased container, but guarantees neither monomorphization
nor zero allocation by captures or the body. Erased Function Types retain published borrow contracts;
allocation depends on representation and optimization, and the Windows profile permits inline storage
([SPEC §7.6.4](spec/07-functions-and-callable-values.md#764-function-references-and-common-type-conversion),
[§8.6](spec/08-generics-constraints-and-contracts.md#86-callable-constraints)).

For example, a search borrows its predicate:

```kimi
func firstIndex<T, F>(values: Slice<T>, matching: ref/F) -> Option<isize>
    F is Callable<(ref/T) -> bool>
    for index in values.indices
        if matching(values[index])
            return .Some(index)
    return .None
```

### 4.4. Explicit Move and Copy

- `[Language]` Transferring a Non-Copy Place explicitly requires `@move`; bare acquisition
  copies only a proven Copy value ([SPEC §3.5](spec/03-types-and-values.md#35-copy-and-move)).
- `[Advice]` Use `@copy` to make an expensive aggregate copy, a generic Copy operation or an acquisition
  mode clear. Prefer bare acquisition for routine scalar reads.

## 5. Failure and construction

### 5.1. Outcomes

`[Kimi]` Choose a result by the caller's recovery needs:

| Situation | Representation | Examples |
| --- | --- | --- |
| Expected absence or completion; no reason is needed | `Option<T>` | `pop`, `next`, `tryGet` |
| Recoverable failure with a reason or rejected inputs | `Result<T, E>` | `validateUtf8`, `tryInsert` |
| Violated precondition | Abort | Invalid index, negative count |
| Unrecoverable resource failure | Abort | Required allocation failure |

- `[Kimi]` Do not use sentinel values for absence. Return `Option<isize>`, not `-1`.
- `[Kimi]` Represent error reasons with a specific enum or struct, not a string. Return rejected owned
  inputs in `Err`; their Type or a Tuple is sufficient when no separate reason is needed.
  `Dictionary.tryInsert` uses `Result<(), (K, V)>`.
- `[Advice]` Prefer a named enum to `Option<bool>` or `Result<(), ()>` when it makes the outcomes clearer.
  Propagate with `try`, handle with `match`, and discard intentionally with `_ =`.

### 5.2. Names, checks and constructors

- `[Kimi]` Use `try` for recoverable failure unless the name already expresses checking (`validateUtf8`).
  An Aborting counterpart, as in `resolve` / `tryResolve`, is optional. Normal absence or completion
  needs no prefix (`firstIndex`, `pop`, `next`). Keep Contract names such as `reserve` and `format`.
- `[Language]` A try-prefixed Kimi API recovers only from its specified outcome, not arbitrary failures
  in argument evaluation or callees
  ([SPEC §4.7.1](spec/04-arrays-indexing-and-slices.md#471-common-acquisition-and-outcomes)).
- `[Kimi]` Validate entry preconditions before mutating state. Checks that require callbacks or traversal
  follow the API's specified effect order. Write `require condition else => $abort("Message")`; use a
  short capitalized phrase naming the violated condition, without a final period.
- `[Kimi]` `init` has no recoverable failure result; invalid preconditions or unrecoverable failures may
  Abort. Recoverable construction uses a Type function returning `Option<Self>` or `Result<Self, E>`.

## 6. Documentation comments

### 6.1. Placement and summary

- `[Kimi]` Document new or changed public declarations with `///`; example omissions follow §0.
- `[Kimi]` Prefer documentation, then Attributes, then the declaration.
- `[Language]` Only the closest documentation candidate at the header's indentation can attach; an empty
  or misindented candidate has no fallback. Attributes may precede, separate or follow documentation
  ([SPEC §2.3.2](spec/02-source-and-lexical-structure.md#232-declaration-association)).
- `[Kimi]` Start with a one-sentence summary: a noun phrase for a Type or a third-person description
  for an operation ("Returns …", "Appends …"). Explain meaning rather than repeating the signature.

### 6.2. Details and examples

`[Kimi]` Separate the summary from details with a blank `///` line. Add the items that apply, using the
[documentation Markdown profile](spec/documentation-markdown.md#3-summary-and-documentation-items):

| Item | Include when |
| --- | --- |
| `- name:` | A parameter's meaning is not obvious; replace `name` with its actual name |
| `- return:` | The result needs explanation, including retained dependencies |
| `- abort:` | The operation has a function-specific Abort condition |
| `- safety:` | An unsafe declaration imposes caller obligations |
| `# example` with a fenced `kimi` block | A usage example clarifies the API |

`[Kimi]` Explain ownership, effects, copying, allocation and nonconstant complexity where relevant.
This hypothetical removal example omits its enclosing Type and body:

```kimi
/// Removes the element at `index` and moves the last element into its place.
///
/// Runs in O(1), allocates no storage and does not preserve order.
///
/// - return: The removed element; the caller takes ownership.
/// - abort: `index < 0` or `index >= self.length`.
public func swapRemove(self: uniq/Self, index: isize) -> T
```

`[Advice]` Put explanations that span several declarations on a group or in a separate Markdown document.
