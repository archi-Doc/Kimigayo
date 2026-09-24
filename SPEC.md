# Kimigayo Language Specification

This is the index of the Kimigayo specification. The chapter files under `spec/` form its body. Headings are numbered **chapter → section → subsection**. Each concept has one owning section; other sections cross-reference it without restating its rules. Edit the owning chapter or appendix; this index adds no rules.

## Normative status

[§1.3](spec/01-overview.md#13-reading-the-rules) defines the normative force of the wording used throughout.

| Part | Status |
| --- | --- |
| Chapters 1–22, the [Documentation Markdown profile](spec/documentation-markdown.md), the [UTF-8 formatting profile](spec/utf8-formatting.md) and the [test execution profile](spec/testing-profile.md) | Normative language rules and implementation contracts. |
| Appendix A | Normative compiler requirements. |
| Appendix B | Non-normative reference models (optional algorithms). |
| Appendix C | Pointer to the separate implementation status; not part of the language. |
| Appendix D | Index of deferred designs and boundaries; the owning sections remain authoritative. |
| Appendix E | Terminology index; the linked definitions remain authoritative. |
| Appendix F | Non-normative syntax summary; the linked language sections remain authoritative. |

Implementation coverage is recorded separately in [STATUS.md](STATUS.md). Parser support alone establishes neither Binding, constant evaluation, ownership checking, nor execution. Implementation limits never narrow the rules of this specification.

Origin schemas, binding sets, projections, relations and completion are defined in [§15.3–4](spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations), with generic reconstruction in [§8.1.2](spec/08-generics-constraints-and-contracts.md#812-reconstruction-and-origin-annotations).

The argument-name boundary (`!`), normalized name contracts and independent argument omission (a default expression) are defined in [§7.2](spec/07-functions-and-callable-values.md#72-parameters-and-defaults), with positional matching in [§10.1](spec/10-overload-resolution-and-inference.md#101-candidate-applicability).

Optional Type spelling is defined in [§3.2.3](spec/03-types-and-values.md#323-optional-type-spelling), explicit discard in [§14.2.4](spec/14-control-flow.md#1424-explicit-discard), and try propagation in [§17.2.4](spec/17-failure-handling.md#1724-try-propagation).

## Contents

### Part I. Introduction and source text

- [1. Overview](spec/01-overview.md)
- [2. Source and lexical structure](spec/02-source-and-lexical-structure.md)
  - [Documentation Markdown profile](spec/documentation-markdown.md): syntax, items, HTML/links, source positions and processing guarantees.

### Part II. Types, values, and sequences

- [3. Types and values](spec/03-types-and-values.md)
- [4. Arrays, indexing, and slices](spec/04-arrays-indexing-and-slices.md)
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
- [14. Control flow](spec/14-control-flow.md)

### Part V. Ownership, cleanup, and failure

- [15. Ownership and lifetime analysis](spec/15-ownership-and-lifetime-analysis.md)
  - [Lending rule](spec/15-ownership-and-lifetime-analysis.md#1515-movable-places): a bare Place never Moves; `@move` transfers, a new exclusive lending of an owned Place needs `@uniq`/`@objuniq` except for a [Receiver Expression](spec/07-functions-and-callable-values.md#73-explicit-receivers), which is acquired implicitly, and borrow values are reborrowed without a spelling.
  - [Subject rule](spec/15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime): `match` Subjects and `for` iterables are shared-borrowed bare or with `@ref`, shared-reborrowed as borrow values, taken by value with `@move`, and never written `@uniq`/`@objuniq`.
  - [Call borrow reservations](spec/15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations): preparation, activation and non-escaping explicit borrows.
- [16. Scope exit and destruction](spec/16-scope-exit-and-destruction.md)
- [17. Failure handling](spec/17-failure-handling.md)

### Part VI. Programs, compilation, and runtime

- [18. Modules and dependencies](spec/18-modules-and-dependencies.md)
- [19. Compile-time directives](spec/19-compile-time-directives.md)
- [20. Compilation configuration](spec/20-compilation-configuration.md)
- [21. Layout, runtime metadata, and code generation](spec/21-layout-runtime-and-code-generation.md)
- [22. Kimi, program execution, and foreign functions](spec/22-core-execution-and-foreign-functions.md)
  - [UTF-8 formatting profile](spec/utf8-formatting.md): buffers, views, formatting, interpolation and required costs.
  - [Test execution profile](spec/testing-profile.md): solution execution, settings, temporary storage, limits, identities and results.

### Appendices

- [Appendix A. Compiler implementation requirements](spec/appendices/A-compiler-requirements.md)
- [Appendix B. Non-normative reference models](spec/appendices/B-reference-models.md)
- [Appendix C. Implementation status](#appendix-c-implementation-status)
- [Appendix D. Deferred feature index](spec/appendices/D-deferred-features.md)
- [Appendix E. Terminology index](spec/appendices/E-terminology.md)
- [Appendix F. Syntax summary](spec/appendices/F-syntax-summary.md)

<a id="appendix-c-implementation-status"></a>

#### Appendix C. Implementation status

Implementation coverage, verified support boundaries and the status of the executable examples are maintained in [STATUS.md](STATUS.md), separately from language conformance. The documented limits of the [examples](examples/) and [milestone programs](milestones/README.md) do not narrow the language rules.

## Where to start

- **Origins:** [schemas, names, relations and canonical contracts](spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations), [postfix borrow attachment](spec/03-types-and-values.md#336-nested-semantics-and-type-grouping), and [completion and omission](spec/15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision).
- **Initial implementation profile:** generics are [monomorphized](spec/21-layout-runtime-and-code-generation.md#2131-policy-and-sharing-conditions); generic code sharing and the Mod host are deferred ([Appendix D](spec/appendices/D-deferred-features.md)).
- **Standard declarations and functions:** [Kimi declaration and function reference](#kimi-declaration-and-function-reference).
- **First executable program:** [minimal console output](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output), [program startup](spec/22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization), and [LLVM output and native build](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution).
- **Source commands:** [input resolution and implicit single-source projects](spec/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects); [lock files](spec/18-modules-and-dependencies.md#185-lock-files-and-input-records) for `restore` and `check --locked`.
- **Milestone programs:** [Milestone1–24](milestones/README.md) are independent programs of increasing difficulty. They progress from Hello World through ownership and lifetimes, control flow and Patterns, arrays, nested Declaration Containers, generics and specialization, closures and captures, Slice/Iterator Contracts, exclusive object creation, ownership joins, external Origin forwarding and ordered whole-value updates to composite generic values, associated-Type Contracts, Type/length/Origin inference, concrete generic generation and Properties. The same README defines a 38-program roadmap, summarizes creation/build/test status, and assigns separate semantic and implementation verification scopes to programs 22–38; 25–38 remain future source targets. Program 20 covers inference/defaults; 21 covers explicit specialization; 22 targets the initial monomorphization profile; 23/24 separate Copy Property access from ownership-bearing operations and Contract witnesses. Source creation does not establish compiler support. Expected behavior follows this specification.

## Kimi declaration and function reference

This table indexes the required declarations in [§22.1](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations). [§22.1.1](spec/22-core-execution-and-foreign-functions.md#2211-declaration-placement-and-function-reference) owns their placement and collects function signatures; the linked operation sections define behavior.

| Declaration container | Declarations / functions | Reference |
| --- | --- | --- |
| `Kimi` | Intrinsic Contracts: `Copy`, `Owned`, `Callable`, `Sealed`, `ObjectPayload` | [§8.4.7](spec/08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees) |
| `Kimi` | Types: `Option<T>`, `Result<T,E>`, `Weak<S>`, `Array<T>`, `Index`, `Range`, `ResolvedRange`, `Slice<T>`, `Dictionary<K,V>`; Contracts: `Equatable`, `Comparable`, `Iterator`, `Iterable` | [§22.1 declaration shapes and member requirements](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) |
| `Kimi` | `Utf8Format`, `BufferWriter`, `WriteWindow`, `Utf8Writer`, `BufferFull` | [Formatting declarations](spec/utf8-formatting.md#1-contracts-and-declarations) |
| `Kimi.Text` | `FixedBuffer`, `HeapBuffer`, `Utf8Slice`, `InvalidUtf8`; `fixed`, `heap`, `writer`, `utf8`, `validateUtf8`, `toString`, `tryFormat` | [Text operations](spec/utf8-formatting.md#2-text-operations) |
| `Kimi.Console` | `writeLine(text: ref/string) -> ()`, `writeLine(text: Text.Utf8Slice) -> ()` | [§22.4](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output) |
| `Kimi.Test` | `tempDirectory() -> string`, in test-only bodies | [Test execution profile](spec/testing-profile.md#environment-and-temporary-directory) |
| `Kimi.Intrinsics` | `replace`, `exchange`, `swap` | [§15.7 whole-value updates](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates) |
| `Kimi.Intrinsics` | `makeObj`, `makeRc`, `makeArc`, strong/Weak `clone`, `downgrade`, `upgrade`, `makeRcCyclic`, `makeArcCyclic` | [§13.5.8–9](spec/13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) |

These are specification requirements, not a list of completed compiler features. [STATUS.md](STATUS.md#kimi-library-and-whole-value-updates) lists current declarations, compiler-supplied helpers such as `SliceIterator`, and runtime limits. `$abort` is a separate language built-in, not a Kimi Function.

## Specification boundaries

The owning formal sections define required behavior. Proposal and migration history is kept separately in [PLAN_HISTORY.md](PLAN_HISTORY.md); it does not override this specification.

The Composition Root Entry/Provider design was withdrawn; its declarations and final selection remain unsettled. [§13.8](spec/13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax) defines the reserved root and the independently specified built-ins; it neither redirects `Kimi.Console.writeLine` nor introduces composition Bindings.

Deferred implementation work that awaits explicit instructions, including the [ObjectCallCompatible plan](spec/appendices/D-deferred-features.md#objectcallcompatible), is recorded in [Appendix D](spec/appendices/D-deferred-features.md).
