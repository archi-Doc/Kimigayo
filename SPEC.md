# Kimigayo Language Specification

This is the index of the Kimigayo specification. The chapter files under `spec/` form its body, organized into six parts. Numbered headings retain **chapter → section → subsection** identifiers. Each concept has an owning section; cross-references apply its rules without redefining them.

Chapters 1–22 define language rules and implementation contracts; [§1.3](spec/01-overview.md#13-reading-the-rules) explains their normative force. Appendix A contains normative compiler requirements, B contains optional reference models, D records deferred boundaries, E indexes terminology, and F is a non-normative syntax summary whose linked language sections remain authoritative. Appendix C below points to the separate implementation status. Edit the owning chapter or appendix; this index does not duplicate their rules.

Design documents, decision records, and change records are stored in `draft/` (formerly `doc/`). The directory rename does not change the scope of existing precedence notices, including those below.

The [whole-value replacement change](draft/Changes/2026-09-17%20Whole%20Value%20Replacement.md) is integrated into [Sealed](spec/08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees), [payload projections](spec/13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow), [receiver compatibility](spec/12-expressions.md#1244-object-receiver-compatibility), and [whole-value updates and dependencies](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates), with corresponding destruction, refinement, artifact, and code-generation rules. Its Section 6 changes take precedence; unrelated proposal features are not adopted. See STATUS.md for implementation coverage.

The [Kimi library and named aliases change](draft/Changes/2026-09-17%20Kimi%20Library%20and%20Named%20Aliases.md) is integrated into [source aliases and effective defaults](spec/18-modules-and-dependencies.md#181-external-references-and-aliases), [name lookup and diagnostics](spec/09-names-signatures-and-access.md#941-named-aliases-collisions-and-warnings), and [the Kimi library](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations). Core remains the Type component. Declaration Container nesting is adopted separately below; implementation coverage remains in STATUS.md.

The [Declaration Container nesting change](draft/Changes/2026-09-17%20Declaration%20Container%20Nesting.md) is integrated into [container placement and environments](spec/06-declarations-and-containers.md#611-root-and-nested-containers), [bound Contract references](spec/08-generics-constraints-and-contracts.md#849-bound-contracts-collisions-and-proof-paths), [qualified lookup](spec/09-names-signatures-and-access.md#961-bound-container-paths), and [static storage](spec/22-core-execution-and-foreign-functions.md#2224-static-storage-in-inherited-environments). Its changed rules take precedence over earlier restrictions. Named aliases remain available under Chapter 18; runtime Contract Views and user-declared Contract parameters are not introduced. See [STATUS.md](STATUS.md) for verified implementation coverage.

The dependency and artifact specification is integrated into [Chapter 18](spec/18-modules-and-dependencies.md), [native connections and commands](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution), [product/test generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), and [compiler verification requirements](spec/appendices/A-compiler-requirements.md#a16-dependencies-artifacts-and-bounded-reuse). These sections define source-first distribution, exact-version resolution, lock files, pack/publish, content stores, semantic reuse, and native input validation. They incorporate the adopted [dependency/artifact draft](draft/Design/2026-09-13%20Dependencies%20and%20Artifacts.md); implementation coverage remains in STATUS.md.

For a broad, non-normative source walkthrough, see the [specification tour](examples/SpecTour/README.md). It illustrates specified language features beyond current executable support and identifies remaining implementation and language boundaries.

For progressively more demanding, independent programs, see [Milestone1–17](milestones/README.md). They progress from Hello World through ownership/lifetimes to combined control flow and patterns, arrays, nested declaration containers, generic sharing/specialization, closures/captures, Slice/Iterator contracts, exclusive object creation, ownership joins, external Origin forwarding, and ordered whole-value updates. The same README outlines proposed programs 18–34 toward core completion. Expected behavior follows the specification; verified implementation coverage is recorded separately.

Composition Root Entry/Provider declarations and final selection remain unsettled following withdrawal of the design. [§13.8](spec/13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax) defines the reserved root and independently specified built-ins; it does not redirect Kimi.Console.writeLine or introduce composition Bindings. [Appendix D](spec/appendices/D-deferred-features.md) tracks this boundary.

The [testing design](draft/Design/2026-09-13%20Testing.md) is integrated into [Test declarations](spec/06-declarations-and-containers.md#651-test-definitions), [verification and cleanup](spec/17-failure-handling.md#175-test-verification-operations), [inputs](spec/18-modules-and-dependencies.md#188-product-and-test-inputs), [discovery and CLI](spec/20-compilation-configuration.md#209-test-command-and-discovery), [generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), and [execution/reporting](spec/22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting). Appendix A.17 defines compiler verification and Appendix F summarizes syntax. Concrete profile interfaces/defaults and future features remain in [Appendix D.4](spec/appendices/D-deferred-features.md#d4-testing-profile-details-and-extensions). Specification integration does not establish executable support; see [STATUS.md](STATUS.md).

The [test verification rules in §17.5](spec/17-failure-handling.md#175-test-verification-operations) supersede the testing draft's earlier `$require` return behavior: both operations evaluate the condition once; a false `$expect` records failure and continues, while a false `$require` records failure and initiates Abort after condition/message temporary cleanup. The current case's child process terminates without subsequent ordinary cleanup; the runner retains the failure and termination reason under §22.6.

For the first executable program, start with [minimal console output](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output), [program startup](spec/22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization), and [LLVM output/native build](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution). The language rules below remain distinct from the implementation milestone in [STATUS.md](STATUS.md#c12-first-executable-milestone).

The source commands share [input resolution and implicit single-source projects](spec/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects), including extensionless project/source lookup and Application/O2 defaults. See [dependency resolution and lock lifecycle](spec/18-modules-and-dependencies.md#185-lock-files-and-input-records) for `restore` and `check --locked`; implementation coverage remains in [STATUS.md](STATUS.md).

## Deferred

The implementation has been planned, but we have intentionally not started it yet. We will begin executing the plan when the appropriate time comes. Until then, please wait for further instructions.

### ObjectCallCompatible

The [inference and call rules](spec/12-expressions.md#1244-object-receiver-compatibility) and [public summary requirements](spec/18-modules-and-dependencies.md#187-verified-information-and-reuse) include the complete Sealed payload exception. Full public-effect inference and release checking will proceed in three stages:

1. **Document the plan (current stage).** Preserve the existing specification; make no implementation changes.
2. **Implement inference and publication.** Verify bodies, callees, and all explicit specializations; publish completed `Proven` / `NotProven` results in public API information and enforce the existing call rules.
3. **Check release compatibility.** Compare corresponding public APIs with a previous published Package version and report `Proven` to `NotProven` as a breaking change. Baseline selection, API matching, and release enforcement remain to be specified.

This plan adds no Attribute or changes to compatibility rules. Stages 2 and 3 await further instructions.

## Part I. Introduction and source text

- [1. Overview](spec/01-overview.md)
- [2. Source and lexical structure](spec/02-source-and-lexical-structure.md)

## Part II. Types, values, and sequences

- [3. Types and values](spec/03-types-and-values.md)
- [4. Arrays, indexing, and slices](spec/04-arrays-indexing-and-slices.md)
- [5. Raw pointers and unsafe memory](spec/05-raw-pointers-and-unsafe-memory.md)

## Part III. Declarations and static semantics

- [6. Declarations and containers](spec/06-declarations-and-containers.md)
- [7. Functions and callable values](spec/07-functions-and-callable-values.md)
- [8. Generics, constraints, and contracts](spec/08-generics-constraints-and-contracts.md)
- [9. Names, signatures, and access](spec/09-names-signatures-and-access.md)
- [10. Overload resolution and inference](spec/10-overload-resolution-and-inference.md)
- [11. Properties](spec/11-properties.md)

Receiver shorthand is defined in [§7.3](spec/07-functions-and-callable-values.md#73-explicit-receivers) for bare `self` in instance functions and [§11.2](spec/11-properties.md#112-accessor-functions) for omitted accessor receivers.

## Part IV. Expressions and control flow

- [12. Expressions](spec/12-expressions.md)
- [13. Operators and assignment](spec/13-operators-and-assignment.md)
- [14. Control flow](spec/14-control-flow.md)

## Part V. Ownership, cleanup, and failure

- [15. Ownership and lifetime analysis](spec/15-ownership-and-lifetime-analysis.md)
- [16. Scope exit and destruction](spec/16-scope-exit-and-destruction.md)
- [17. Failure handling](spec/17-failure-handling.md)

## Part VI. Programs, compilation, and runtime

- [18. Modules and dependencies](spec/18-modules-and-dependencies.md)
- [19. Compile-time directives](spec/19-compile-time-directives.md)
- [20. Compilation configuration](spec/20-compilation-configuration.md)
- [21. Layout, runtime metadata, and code generation](spec/21-layout-runtime-and-code-generation.md)
- [22. Kimi, program execution, and foreign functions](spec/22-core-execution-and-foreign-functions.md)

## Appendices

- [Appendix A. Compiler implementation requirements](spec/appendices/A-compiler-requirements.md)
- [Appendix B. Non-normative reference models](spec/appendices/B-reference-models.md)
- [Appendix C. Implementation status](#appendix-c-implementation-status)
- [Appendix D. Deferred feature index](spec/appendices/D-deferred-features.md)
- [Appendix E. Terminology index](spec/appendices/E-terminology.md)
- [Appendix F. Syntax summary](spec/appendices/F-syntax-summary.md)

### Appendix C. Implementation status

Implementation coverage is maintained in [STATUS.md](STATUS.md), separately from language conformance. Parser support alone establishes neither Binding, constant evaluation, ownership checking, nor execution.

Numeric adaptation rules are owned by [§13.5.4](spec/13-operators-and-assignment.md#1354-numeric-conversions-and-literals), the Windows 128-bit boundary by [§21.5.3](spec/21-layout-runtime-and-code-generation.md#2153-checked-instructions-and-raw-pointers), and runtime failure codes by [§22.5.4](spec/22-core-execution-and-foreign-functions.md#2254-abort-diagnostics-and-exit).

Same-Type acquisition follows [§13.5.3](spec/13-operators-and-assignment.md#1353-defined-adaptations). Fixed-array Constant-readable Bindings and typed checked length evaluation are defined in [§4.2](spec/04-arrays-indexing-and-slices.md#42-length-constants); they do not extend ordinary constant evaluation or static index disjointness.

Declaration-fragment identity, matching headers, accessibility defaults, and shared base clauses follow [§6.1.2](spec/06-declarations-and-containers.md#612-container-fragments). A synthesized path group is not an independent header fragment.

Executable aggregate examples include [Copy element reads](examples/ElementReads/README.md), [Copy element assignments](examples/ElementAssignments/README.md), [numeric element updates](examples/ElementUpdates/README.md), [disjoint element paths](examples/ElementPaths/README.md), [owned Non-Copy element replacements](examples/ElementReplacements/README.md), [static element partial Move and repair](examples/ElementMoves/README.md), [shared string element inspection and arguments](examples/ElementBorrows/README.md), [element borrowing from owned parameters and temporaries](examples/ElementBorrowOwners/README.md), and [partial Move from owned parameters](examples/ElementParameterMoves/README.md). Their documented implementation limits do not narrow the language rules in Chapters 4, 13 and 15.

Element writes retain the [exclusive Loan required by indexing](spec/04-arrays-indexing-and-slices.md#464-bounds-evaluation-and-failure) from bounds resolution through old-value destruction and placement. Simple assignment secures its RHS before location; numeric updates read the old value and evaluate their RHS under the established Loan. Aliased access is subject to [Loan conflicts](spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts); current coverage and conservative handling of dynamic indices are recorded in [STATUS.md](STATUS.md).

Static local element Move now tracks construction separately from current completeness, repairs permitted missing paths, and destroys only remaining initialized parts. The executable scope and verification results are documented in STATUS.md §4.8 and §7.4; the language rules remain in [§15.1](spec/15-ownership-and-lifetime-analysis.md#151-initialization-and-consume-analysis) and [§16.3](spec/16-scope-exit-and-destruction.md).
