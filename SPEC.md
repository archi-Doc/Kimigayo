# Kimigayo Language Specification

This is the index of the Kimigayo specification. The chapter files under `spec/` form its body, organized into six parts. Numbered headings retain **chapter → section → subsection** identifiers. Each concept has an owning section; cross-references apply its rules without redefining them.

Chapters 1–22 define language rules and implementation contracts; [§1.3](spec/01-overview.md#13-reading-the-rules) explains their normative force. Appendix A contains normative compiler requirements, B contains optional reference models, D records deferred boundaries, E indexes terminology, and F is a non-normative syntax summary whose linked language sections remain authoritative. Appendix C below points to the separate implementation status. Edit the owning chapter or appendix; this index does not duplicate their rules.

Design documents, decision records, and change records are stored in `draft/` (formerly `doc/`). The directory rename does not change the scope of existing precedence notices, including those below.

The dependency and artifact specification is integrated into [Chapter 18](spec/18-modules-and-dependencies.md), [native connections and commands](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution), [product/test generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), and [compiler verification requirements](spec/appendices/A-compiler-requirements.md#a16-dependencies-artifacts-and-bounded-reuse). These sections define source-first distribution, exact-version resolution, lock files, pack/publish, content stores, semantic reuse, and native input validation. They incorporate the adopted [dependency/artifact draft](draft/Design/2026-09-13%20Dependencies%20and%20Artifacts.md); implementation coverage remains in STATUS.md.

For a broad, non-normative source walkthrough, see the [specification tour](examples/SpecTour/README.md). It illustrates specified language features beyond current executable support and identifies features whose source APIs remain undefined.

The adopted [Composition Root specification](draft/Decisions/2026-09-13%20Composition%20Root%20Review.md) takes precedence over this document for root operations, Entry declarations and references, Provider selection, final composition, and composition-dependent artifacts. It fixes root functions as nonreplaceable, connects Core.writeLine to the selected Std Entry, and separates module identity from final composition. Detailed integration into the sections and grammar below remains pending; this is specified design, not executable support.

The adopted [testing specification](draft/Design/2026-09-13%20Testing.md) takes precedence for test-mode language/execution rules: `#Test`, `$expect`, `$require`, and `kimi test`. It defines common verification/cleanup, process isolation, and bounded reporting/recovery. Product/test input and generation boundaries are integrated in §18.8 and §21.3.7; remaining test syntax/execution integration is pending. This records design, not executable support; see [STATUS.md](STATUS.md).

For the first executable program, start with [minimal console output](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output), [program startup](spec/22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization), and [LLVM output/native build](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution). The language rules below remain distinct from the implementation milestone in [STATUS.md](STATUS.md#c12-first-executable-milestone).

The `build`, `emit`, and `run` commands share [input resolution and implicit single-source projects](spec/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects), including extensionless project/source lookup, Application/O2 defaults, and a concise project-settings summary before execution.

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
- [22. Core, program execution, and foreign functions](spec/22-core-execution-and-foreign-functions.md)

## Appendices

- [Appendix A. Compiler implementation requirements](spec/appendices/A-compiler-requirements.md)
- [Appendix B. Non-normative reference models](spec/appendices/B-reference-models.md)
- [Appendix C. Implementation status](#appendix-c-implementation-status)
- [Appendix D. Deferred feature index](spec/appendices/D-deferred-features.md)
- [Appendix E. Terminology index](spec/appendices/E-terminology.md)
- [Appendix F. Syntax summary](spec/appendices/F-syntax-summary.md)

### Appendix C. Implementation status

Implementation coverage is maintained in [STATUS.md](STATUS.md), separately from language conformance. Parser support alone establishes neither Binding, constant evaluation, ownership checking, nor execution.

Executable aggregate examples include [Copy element reads](examples/ElementReads/README.md), [Copy element assignments](examples/ElementAssignments/README.md), and [numeric element updates](examples/ElementUpdates/README.md). Their documented implementation limits do not narrow the language rules in Chapters 4, 13 and 15.

Numeric element updates retain the [exclusive Loan required by indexing](spec/04-arrays-indexing-and-slices.md#464-bounds-evaluation-and-failure) through writeback. Aliased RHS access is subject to [Loan conflicts](spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts); current implementation coverage and conservative root-level restrictions are recorded in [STATUS.md](STATUS.md).
