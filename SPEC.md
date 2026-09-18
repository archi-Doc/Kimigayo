# Kimigayo Language Specification

This is the index of the Kimigayo specification. The chapter files under `spec/` form its body. Headings are numbered **chapter → section → subsection**. Each concept has one owning section; other sections cross-reference it without restating its rules. Edit the owning chapter or appendix; this index adds no rules.

## Normative status

[§1.3](spec/01-overview.md#13-reading-the-rules) defines the normative force of the wording used throughout.

| Part | Status |
| --- | --- |
| Chapters 1–22 | Normative language rules and implementation contracts. |
| Appendix A | Normative compiler requirements. |
| Appendix B | Non-normative reference models (optional algorithms). |
| Appendix C | Pointer to the separate implementation status; not part of the language. |
| Appendix D | Index of deferred designs and boundaries; the owning sections remain authoritative. |
| Appendix E | Terminology index; the linked definitions remain authoritative. |
| Appendix F | Non-normative syntax summary; the linked language sections remain authoritative. |

Implementation coverage is recorded separately in [STATUS.md](STATUS.md). Parser support alone establishes neither Binding, constant evaluation, ownership checking, nor execution. Implementation limits never narrow the rules of this specification.

## Contents

### Part I. Introduction and source text

- [1. Overview](spec/01-overview.md)
- [2. Source and lexical structure](spec/02-source-and-lexical-structure.md)

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
- [16. Scope exit and destruction](spec/16-scope-exit-and-destruction.md)
- [17. Failure handling](spec/17-failure-handling.md)

### Part VI. Programs, compilation, and runtime

- [18. Modules and dependencies](spec/18-modules-and-dependencies.md)
- [19. Compile-time directives](spec/19-compile-time-directives.md)
- [20. Compilation configuration](spec/20-compilation-configuration.md)
- [21. Layout, runtime metadata, and code generation](spec/21-layout-runtime-and-code-generation.md)
- [22. Kimi, program execution, and foreign functions](spec/22-core-execution-and-foreign-functions.md)

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

- **Standard declarations and functions:** [Kimi declaration and function reference](#kimi-declaration-and-function-reference).
- **First executable program:** [minimal console output](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output), [program startup](spec/22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization), and [LLVM output and native build](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution).
- **Source commands:** [input resolution and implicit single-source projects](spec/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects); [lock files](spec/18-modules-and-dependencies.md#185-lock-files-and-input-records) for `restore` and `check --locked`.
- **Milestone programs:** [Milestone1–21](milestones/README.md) are independent programs of increasing difficulty. They progress from Hello World through ownership and lifetimes, control flow and Patterns, arrays, nested Declaration Containers, generic sharing and specialization, closures and captures, Slice/Iterator Contracts, exclusive object creation, ownership joins, external Origin forwarding and ordered whole-value updates to composite generic values, associated-Type Contracts, and Type/length/Origin inference with full specialization. The same README defines a 38-program roadmap, summarizes creation/build/test status, and assigns separate semantic and implementation verification scopes to future programs 22–38. Program 20 covers inference/defaults; program 21 separately covers explicit specialization. Expected behavior follows this specification.

## Kimi declaration and function reference

This table indexes the required declarations in [§22.1](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations). [§22.1.1](spec/22-core-execution-and-foreign-functions.md#2211-declaration-placement-and-function-reference) owns their placement and collects function signatures; the linked operation sections define behavior.

| Declaration container | Declarations / functions | Reference |
| --- | --- | --- |
| `Kimi` | Intrinsic Contracts: `Copy`, `Owned`, `Callable`, `Sealed` | [§8.4.7](spec/08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees) |
| `Kimi` | Types: `Option<T>`, `Result<T,E>`, `Weak<S>`, `Array<T>`, `Index`, `Range`, `ResolvedRange`, `Slice<T>`, `Dictionary<K,V>`; Contracts: `Stringify`, `Equatable`, `Comparable`, `Iterator`, `Iterable` | [§22.1 declaration shapes and member requirements](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) |
| `Kimi.Console` | `writeLine(text: string) -> ()` | [§22.4](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output) |
| `Kimi.Intrinsics` | `replace`, `exchange`, `swap` | [§15.7 whole-value updates](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates) |
| `Kimi.Intrinsics` | `makeObj`, `makeRc`, `makeArc`, strong/Weak `clone`, `downgrade`, `upgrade`, `makeRcCyclic`, `makeArcCyclic` | [§13.5.8–9](spec/13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) |

These are specification requirements, not a list of completed compiler features. [STATUS.md](STATUS.md#kimi-library-and-whole-value-updates) lists current declarations, compiler-supplied helpers such as `SliceIterator`, and runtime limits. `$abort` is a separate language built-in, not a Kimi Function.

## Integrated design records

Design documents, decision records and change records are stored in `draft/` (formerly `doc/`); the rename does not change the precedence notices below. Each record listed here is integrated into the named sections. Where a record states that its changes take precedence, they override earlier restrictions. Features of a record that are not integrated are not adopted.

| Record | Integrated into | Notes |
| --- | --- | --- |
| [Whole-value replacement](draft/Changes/2026-09-17%20Whole%20Value%20Replacement.md) | [Sealed](spec/08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees), [payload projection](spec/13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow), [receiver compatibility](spec/12-expressions.md#1244-object-receiver-compatibility), [whole-value updates](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates), and the corresponding destruction, refinement, artifact and code-generation rules | Its Section 6 changes take precedence. |
| [Kimi library and named aliases](draft/Changes/2026-09-17%20Kimi%20Library%20and%20Named%20Aliases.md) | [Source aliases and effective defaults](spec/18-modules-and-dependencies.md#181-external-references-and-aliases), [name lookup](spec/09-names-signatures-and-access.md#941-named-aliases-collisions-and-warnings), [the Kimi library](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) | Core remains the name of the Type component. |
| [Declaration Container nesting](draft/Changes/2026-09-17%20Declaration%20Container%20Nesting.md) | [Container placement](spec/06-declarations-and-containers.md#611-root-and-nested-containers), [bound Contract references](spec/08-generics-constraints-and-contracts.md#849-bound-contracts-collisions-and-proof-paths), [qualified lookup](spec/09-names-signatures-and-access.md#961-bound-container-paths), [static storage](spec/22-core-execution-and-foreign-functions.md#2224-static-storage-in-inherited-environments) | Its changed rules take precedence. Runtime Contract Views and user-declared Contract parameters are not introduced. |
| [Dependencies and artifacts](draft/Design/2026-09-13%20Dependencies%20and%20Artifacts.md) | [Chapter 18](spec/18-modules-and-dependencies.md), [native build and commands](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution), [product/test generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), [Appendix A.16](spec/appendices/A-compiler-requirements.md#a16-dependencies-artifacts-and-bounded-reuse) | Source-first distribution, exact-version resolution, locks, pack/publish, content stores, semantic reuse and native input validation. |
| [Testing](draft/Design/2026-09-13%20Testing.md) | [Test declarations](spec/06-declarations-and-containers.md#651-test-definitions), [verification](spec/17-failure-handling.md#175-test-verification-operations), [inputs](spec/18-modules-and-dependencies.md#188-product-and-test-inputs), [discovery and CLI](spec/20-compilation-configuration.md#209-test-command-and-discovery), [generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), [execution and reporting](spec/22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting), [Appendix A.17](spec/appendices/A-compiler-requirements.md#a17-test-verification-and-runner-requirements) | §17.5 supersedes the draft's earlier `$require` return behavior. Profile interfaces and future features remain in [Appendix D.4](spec/appendices/D-deferred-features.md#d4-testing-profile-details-and-extensions). |

The Composition Root Entry/Provider design was withdrawn; its declarations and final selection remain unsettled. [§13.8](spec/13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax) defines the reserved root and the independently specified built-ins; it neither redirects `Kimi.Console.writeLine` nor introduces composition Bindings.

Deferred implementation work that awaits explicit instructions, including the [ObjectCallCompatible plan](spec/appendices/D-deferred-features.md#objectcallcompatible), is recorded in [Appendix D](spec/appendices/D-deferred-features.md).
