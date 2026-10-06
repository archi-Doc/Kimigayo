# Kimigayo Language Specification

This is the index of the Kimigayo language specification; the chapter files under `spec/` form its body. The separate [implementation specification](IMPL.md) continues the same numbering with Chapters 20–21, the test execution profile and Appendices A–B. Headings are numbered **chapter → section → subsection**. Each concept has one owning section, and other sections cross-reference it instead of restating its rules. Changes belong in the owning chapter or appendix: this index adds no rules.

## Normative status

[§1.3](spec/01-overview.md#13-reading-the-rules) defines the normative force of the wording used throughout.

| Part | Status |
| --- | --- |
| Chapters 1–19, 22 and 24, the [Documentation Markdown profile](spec/documentation-markdown.md) and the [UTF-8 formatting profile](spec/utf8-formatting.md) | Normative language rules. |
| Chapter 23 | Normative contracts of the compiler services (check foundation, Language Server Protocol, Compiler Server Protocol). They govern tools and never change language validity. |
| Appendix C | Pointer to the separate implementation status; not part of the language. |
| Appendix D | Index of deferred designs and boundaries; the owning sections remain authoritative. |
| Appendix E | Terminology index; the linked definitions remain authoritative. |
| Appendix F | Non-normative syntax summary; the linked language sections remain authoritative. |

Implementation coverage is recorded separately ([Appendix C](#appendix-c-implementation-status)). Parser support alone establishes neither Binding, constant evaluation, ownership checking nor execution, and implementation limits never narrow the rules of this specification.

## Contents

### Part I. Introduction and source text

- [1. Overview](spec/01-overview.md)
- [2. Source and lexical structure](spec/02-source-and-lexical-structure.md)
  - [Documentation Markdown profile](spec/documentation-markdown.md): syntax, items, HTML/links, source positions and processing guarantees.

### Part II. Types, values, and sequences

- [3. Types and values](spec/03-types-and-values.md)
- [4. Arrays, indexing, and slices](spec/04-arrays-indexing-and-slices.md): local fixed-array holes, `noinit`, shared/exclusive views and copying into Array.
- [5. Raw pointers and unsafe memory](spec/05-raw-pointers-and-unsafe-memory.md)

### Part III. Declarations and static semantics

- [6. Declarations and containers](spec/06-declarations-and-containers.md)
- [7. Functions and callable values](spec/07-functions-and-callable-values.md)
- [8. Generics, constraints, and contracts](spec/08-generics-constraints-and-contracts.md)
- [9. Names, signatures, and access](spec/09-names-signatures-and-access.md)
- [10. Overload resolution and inference](spec/10-overload-resolution-and-inference.md)
- [11. Properties](spec/11-properties.md)

### Part IV. Expressions and control flow

- [12. Expressions](spec/12-expressions.md)
- [13. Operators and assignment](spec/13-operators-and-assignment.md)
  - [Pair layers](spec/13-operators-and-assignment.md#pair-layers): a pair `s/T` or `s/U` whose admitted set lies in `value or valueborrow` is a safe value-reference layer that may or may not exist; `@follow` selects its direct target, and every operation applies the rule of each admitted Semantics, taking the weakest capability and mode and keeping each case's dependencies conditionally.
  - [Integer results](spec/13-operators-and-assignment.md#133-arithmetic-bitwise-and-shift-operators) and [wrapping integers](spec/03-types-and-values.md#3111-wrapping-integer-types): one rule computes the mathematical result; an integer Type Aborts on an unrepresentable result, `Wrapping<T>` wraps, and undefined inputs Abort for both. [Numeric, wrapping and bit conversions](spec/13-operators-and-assignment.md#1354-numeric-conversions-and-literals): `@Type` never wraps, `@wrap<U>` wraps, `@bits<U>` reinterprets bits, and direct literals convert once from their exact value.
- [14. Control flow](spec/14-control-flow.md)
  - [Contextual labels](spec/14-control-flow.md#144-labels) and [named transfers](spec/14-control-flow.md#1451-syntax-and-operands): `label name: do` and `exit to name value`.

### Part V. Ownership, cleanup, and failure

- [15. Ownership and lifetime analysis](spec/15-ownership-and-lifetime-analysis.md)
  - [Lending rule](spec/15-ownership-and-lifetime-analysis.md#1515-movable-places): [bare acquisition](spec/03-types-and-values.md#35-copy-and-move) Copies a proven-Copy Place, Reborrows a stored exclusive reference in its own Semantics and never Moves, and `@move` is the only transfer. A new exclusive borrow of an owned Place or temporary needs `@uniq`/`@objuniq`, except for a [Receiver Expression](spec/07-functions-and-callable-values.md#73-explicit-receivers), which is acquired implicitly. `@ref`/`@uniq` borrow the written slot, and `@follow` selects the referent. At a fixed expected Type, the [common adaptation](spec/10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) shared-borrows a readable Place storing `U` (never a reference or handle slot) or an owner temporary for an expected `ref/U`, Reborrows a borrow value, yields one shared reference through nested reference layers and value-reads a reference chain ending in a read Type (a Scalar, position or range Type, [§3.5.3](spec/03-types-and-values.md#353-value-read)). Overload resolution never chooses how a bare Place argument is acquired: the functions of one Name acquire corresponding parameters of overlapping Types in one mode ([parameter acquisition shape](spec/07-functions-and-callable-values.md#731-parameter-acquisition-shape)), so the Name and the position fix it.
  - [Subject rule](spec/15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime): a `match` or `for` Subject is acquired as written, like any other expression, except that a bare Place is borrowed in place. The Subject mode is Shared for a shared borrow or a `ref`/`objref` value, Exclusive for an exclusive borrow or a `uniq`/`objuniq` value, and ByValue for an owned value, such as a temporary, `E@copy` or `E@move` of an owned Place; a transferred or returned reference keeps its own mode. Shared and exclusive paths bind `ref/T` and `uniq/T`, an owned Subject transfers its parts, and guard candidates are `ref/T`.
  - [Call borrow reservations](spec/15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations): reservation, preparation, activation and abandoned calls.
- [16. Scope exit and destruction](spec/16-scope-exit-and-destruction.md)
  - [Drop declarations and obligations](spec/16-scope-exit-and-destruction.md#163-aggregate-destruction-and-drop); `defer` registers cleanup in the enclosing execution scope.
- [17. Failure handling](spec/17-failure-handling.md)

### Part VI. Programs, compilation, and runtime

- [18. Modules and dependencies](spec/18-modules-and-dependencies.md)
- [19. Compile-time directives](spec/19-compile-time-directives.md)
- [22. Kimi, program execution, and foreign functions](spec/22-core-execution-and-foreign-functions.md)
  - [UTF-8 formatting profile](spec/utf8-formatting.md): buffers, views, formatting, interpolation and required costs.
  - [Windows APIs and elapsed time](spec/22-core-execution-and-foreign-functions.md#227-windows-apis-and-elapsed-time): native counter imports, Duration and Stopwatch.

Chapter 20 (compilation configuration), Chapter 21 (layout, runtime metadata and code generation) and the test execution profile belong to the [implementation specification](IMPL.md).

### Part VII. Compiler services

- [23. Compiler services](spec/23-compiler-services.md)
  - [Check foundation](spec/23-compiler-services.md#233-check-foundation): check units, the shared check entry, outcomes and acceptance, input identity, revisions and diagnostics (categories, records, source tables, prerequisites and suppression, limits, order, rendering, the JSON document of `kimi check --Format json` and [repair candidates](spec/23-compiler-services.md#23369-repair-candidates) with their closed conditions).
  - [Language Server Protocol](spec/23-compiler-services.md#234-language-server-protocol): `kimi lsp` transport, document synchronization, discovery, units, change detection, scheduling, publication, [code actions](spec/23-compiler-services.md#2348-code-actions), [Hover](spec/lsp-hover.md) and settings.
  - [Compiler Server Protocol](spec/23-compiler-services.md#235-compiler-server-protocol): purpose, the provided foundation and the requirements of the future protocol.

### Part VIII. Asynchronous execution

- [24. Suspension and asynchronous tasks](spec/24-suspension-and-asynchronous-tasks.md)
  - [Task slots](spec/24-suspension-and-asynchronous-tasks.md#242-task-slots) and [task calls](spec/24-suspension-and-asynchronous-tasks.md#243-task-calls): a function may suspend only when its parameter list begins with `task;`, a call passes the task as `task;`, and tasks on one thread interleave only at task calls; the `Kimi.Async` operations are in [§22.1.3](spec/22-core-execution-and-foreign-functions.md#2213-asynchronous-tasks).

### Appendices

- Appendices A (compiler implementation requirements) and B (reference models): [implementation specification](IMPL.md)
- [Appendix C. Implementation status](#appendix-c-implementation-status)
- [Appendix D. Deferred feature index](spec/appendices/D-deferred-features.md)
- [Appendix E. Terminology index](spec/appendices/E-terminology.md)
- [Appendix F. Syntax summary](spec/appendices/F-syntax-summary.md)

<a id="appendix-c-implementation-status"></a>

#### Appendix C. Implementation status

[STATUS.md](STATUS.md) records implementation coverage, verified support boundaries and the status of the executable examples, separately from language conformance. The documented limits of the [examples](examples/) and [milestone programs](../tests/milestones/README.md) do not narrow the language rules.

## Where to start

- **Origins:** [schemas, names, relations, binding sets, projections and canonical contracts](spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations), [postfix borrow attachment](spec/03-types-and-values.md#336-nested-semantics-and-type-grouping), [completion and omission](spec/15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), and [generic reconstruction](spec/08-generics-constraints-and-contracts.md#812-reconstruction-and-origin-annotations).
- **Places, borrowing and iteration:** [Places and their capabilities](spec/03-types-and-values.md#34-values-places-and-storage), [Place results](spec/07-functions-and-callable-values.md#711-place-results), the [Indexable Contracts](spec/04-arrays-indexing-and-slices.md#469-indexable-contracts), [Origin-parameterized associated Types](spec/08-generics-constraints-and-contracts.md#843-associated-types), and the [iteration Contracts, adapters and storage boundary](spec/22-core-execution-and-foreign-functions.md#2212-iteration-and-storage).
- **Parameters and arguments:** the argument-name boundary (`!`), normalized name contracts and independent defaults in [§7.2](spec/07-functions-and-callable-values.md#72-parameters-and-defaults), one receiver shape and one acquisition mode per position in [§7.3](spec/07-functions-and-callable-values.md#73-explicit-receivers) and [§7.3.1](spec/07-functions-and-callable-values.md#731-parameter-acquisition-shape), and positional matching in [§10.1](spec/10-overload-resolution-and-inference.md#101-candidate-applicability).
- **Optional Types and failure:** [Optional Type spelling](spec/03-types-and-values.md#323-optional-type-spelling), [explicit discard](spec/14-control-flow.md#1424-explicit-discard) and [try propagation](spec/17-failure-handling.md#1724-try-propagation).
- **Initial implementation profile:** generics are [monomorphized](impl/21-layout-runtime-and-code-generation.md#2131-policy-and-sharing-conditions); generic code sharing and the Mod host are deferred ([Appendix D](spec/appendices/D-deferred-features.md)).
- **Asynchronous tasks:** [task slots and task calls](spec/24-suspension-and-asynchronous-tasks.md), the [task boundary, `Async.run`, structured operations, cancellation and channels](spec/22-core-execution-and-foreign-functions.md#2213-asynchronous-tasks), and [task lowering](impl/21-layout-runtime-and-code-generation.md#216-tasks).
- **Standard declarations and functions:** [Kimi declaration and function reference](#kimi-declaration-and-function-reference).
- **First executable program:** [minimal console output](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output), [program startup](spec/22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization), and [LLVM output and native build](impl/20-compilation-configuration.md#208-llvm-output-native-build-and-execution).
- **Source commands:** [input resolution and implicit single-source projects](impl/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects); [lock files](spec/18-modules-and-dependencies.md#185-lock-files-and-input-records) for `restore` and `check --locked`.
- **Editor and tool integration:** the [language server](spec/23-compiler-services.md#234-language-server-protocol) publishes the diagnostics of the shared [check foundation](spec/23-compiler-services.md#233-check-foundation), which the future [Compiler Server Protocol](spec/23-compiler-services.md#235-compiler-server-protocol) also uses.
- **Milestone programs:** the [milestone roadmap](../tests/milestones/README.md) plans 42 independent programs of increasing difficulty, from Hello World through ownership and lifetimes, control flow and Patterns, arrays, generics and specialization, closures, iteration, collections, Properties, formatting and objects. Every program has a source file, and the README records their status. Source creation does not establish compiler support, and expected behavior follows this specification.

## Kimi declaration and function reference

This table indexes the required declarations of [§22.1](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations). [§22.1.1](spec/22-core-execution-and-foreign-functions.md#2211-declaration-placement-and-function-reference) owns their placement and collects the function signatures; the linked sections define behavior.

| Declaration container | Declarations / functions | Reference |
| --- | --- | --- |
| `Kimi` | Intrinsic Contracts: `Copy`, `Owned`, `Callable`, `Sealed`, `ObjectPayload`, `PrimitiveInteger`; closed Contracts: `Position`, `PositionRange` | [§8.4.7](spec/08-generics-constraints-and-contracts.md#847-intrinsic-and-closed-contracts) |
| `Kimi` | Types: `Option<T>`, `Result<T,E>`, `Weak<s/T>`, `Array<T>`, `FromEnd<T>`, `Start`, `End`, `Range<S, E>`, `ClosedRange<S, E>`, `ResolvedRange`, `RangeIterator<T>`, `ClosedRangeIterator<T>`, `Slice<T>`, `UniqSlice<T>`, `Dictionary<K,V>`, `Loan<T>`; Contracts: `Equatable`, `Comparable`, `LendingIterator`, `Iterator`, `Iterable`, `UniqIterable`, `IntoIterable`, `Indexable<Key>`, `UniqIndexable<Key>` | [§22.1 declaration shapes and member requirements](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations), [§22.1.2 iteration and indexing](spec/22-core-execution-and-foreign-functions.md#2212-iteration-and-storage) |
| `Kimi.Iteration` | `OwningIterator<I>`, `BorrowingIterator<I>`; `owning`, `borrowing` | [§22.1.2.3 standard adapters](spec/22-core-execution-and-foreign-functions.md#22123-standard-adapters) |
| `Kimi.Raw` | `allocate`, `release`, `initialize`, `slice` | [§5.6 raw storage operations](spec/05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations) |
| `Kimi.Windows`, `Kimi.Time` | Windows counter imports; `Duration`, `Stopwatch` | [§22.7 Windows APIs and elapsed time](spec/22-core-execution-and-foreign-functions.md#227-windows-apis-and-elapsed-time) |
| `Kimi.Storage` (internal) | `RefRemainder<S>`, `UniqRemainder<S>`, `OwnedRemainder<S>`; `borrowStorage`, `borrowStorageUniq`, `ownStorage`, `splitFirst`, `takeFirst` | [§22.1.2.5 storage boundary](spec/22-core-execution-and-foreign-functions.md#22125-standard-storage-boundary) |
| `Kimi.Async` | Types: `Cancelled`, `SendFailure`, `Raced<A, B>`, `Sender<T>`, `Receiver<T>`; `run`, `sleep`, `checkpoint`, `shield`, `join`, `joinOk`, `race`, `each`, `eachReceived`, `pipe`, `offload`, `offloadEach` | [§22.1.3 asynchronous tasks](spec/22-core-execution-and-foreign-functions.md#2213-asynchronous-tasks) |
| `Kimi.TaskBoundary` (internal) | `enter`, `current`, `park`, `resume` | [§22.1.3.1 task boundary](spec/22-core-execution-and-foreign-functions.md#22131-task-boundary) |
| `Kimi` | `Utf8Format`, `BufferWriter`, `WriteWindow`, `Utf8Writer`, `BufferFull` | [Formatting declarations](spec/utf8-formatting.md#1-contracts-and-declarations) |
| `Kimi.Text` | `FixedBuffer`, `HeapBuffer`, `Utf8Slice`, `InvalidUtf8`; `fixed`, `heap`, `writer`, `utf8`, `validateUtf8`, `toString`, `tryFormat` | [Text operations](spec/utf8-formatting.md#2-text-operations) |
| `Kimi.Console` | `writeLine(text: ref/string) -> ()`, `writeLine(text: Text.Utf8Slice) -> ()` | [§22.4](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output) |
| `Kimi.Test` | `tempDirectory() -> string`, in test-only bodies | [Test execution profile](impl/testing-profile.md#environment-and-temporary-directory) |
| `Kimi.Intrinsics` | `replace`, `exchange`, `swap` | [§15.7 whole-value updates](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates) |
| `Kimi.Intrinsics` | `makeObj`, `makeRc`, `makeArc`, strong/Weak `clone`, `downgrade`, `upgrade`, `makeRcCyclic`, `makeArcCyclic` | [§13.5.8–9](spec/13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) |

These are specification requirements, not a list of completed compiler features. [STATUS.md](STATUS.md#kimi-library-and-whole-value-updates) lists the current declarations, compiler-supplied iterators and runtime limits. `$abort` is a separate language built-in, not a Kimi function.

## Specification boundaries

The owning sections define required behavior. Proposal and migration history is kept in [PLAN_HISTORY.md](dev/PLAN_HISTORY.md) and does not override this specification.

The Composition Root Entry/Provider design was withdrawn; its declarations and final selection remain unsettled. [§13.8](spec/13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax) defines the reserved root and the independently specified built-ins; it neither redirects `Kimi.Console.writeLine` nor introduces composition Bindings.

[Appendix D](spec/appendices/D-deferred-features.md) records deferred implementation work that awaits explicit instructions, including the release-comparison stage of the [ObjectCallCompatible plan](spec/appendices/D-deferred-features.md#objectcallcompatible).

Toolchain verification and original-source O0/O2 integration checks follow [§20.8.5](impl/20-compilation-configuration.md#2085-explicit-toolchain-verification). Compilation success does not certify toolchain identity.

Callable effect bounds are specified in §8.4.10.7; Transferable in §15.2.4 and structured worker offload in §22.1.3.7. Tasks and workers are specified but their implementation is deferred by explicit instruction (Appendix D.5.2).
