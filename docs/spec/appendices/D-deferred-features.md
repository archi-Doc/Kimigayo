# Appendix D. Deferred features

[Specification index](../../SPEC.md)

Each line names a feature outside this revision ([§1.3](../01-overview.md#13-normative-status-and-wording)) and the section that owns its boundary. Implementation status is recorded only in [STATUS.md](../../STATUS.md).

## D.1. Types, values and objects

- Weak extensions: direct Weak view conversion, liveness-only tests, unsafe weak pointers, unowned references. [§3.2.2](../03-types-and-values.md#322-weak-references), [§13.6.3](../13-operators-and-assignment.md#1363-weak-references)
- Implicit or additional ordinary base conversions. [§3.3.1](../03-types-and-values.md#331-object-views-and-identity)
- Runtime Contract Views: designations and spellings, View associated-Type bindings, exact and Contract tests, checked casts; consuming, generic or Type-function runtime requirements. [§8.12](../08-generics-constraints-and-contracts.md#812-runtime-contracts), [§13.6.5](../13-operators-and-assignment.md#1365-checked-casts-and-target-completion)
- Following object and `raw` pair layers (`s is object`, `objectborrow` or an admitted `raw`). [§13.5.5](../13-operators-and-assignment.md#1355-follow)
- Recoverable object creation, allocator selection, general value duplication. [§13.6.2](../13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle), [§3.5.2](../03-types-and-values.md#352-copy-capability)
- Fixed-array element-generator construction. [§4.3](../04-arrays-indexing-and-slices.md#43-array-construction)
- String indexing. [§4.7](../04-arrays-indexing-and-slices.md#47-indexing)
- Descending, stepped and infinite ranges, including iteration of a start-only range; positions that choose their direction at run time; user-defined position and range Types; range operations such as `contains`; position normalization for user Types with a length; default Contract implementations and runtime range Views. [§4.6.1](../04-arrays-indexing-and-slices.md#461-positions), [§4.6.2](../04-arrays-indexing-and-slices.md#462-ranges)
- UniqSlice iteration conformances, dedicated disjoint splits, new try-prefixed operations, public construction from an arbitrary pointer. [§4.8.4](../04-arrays-indexing-and-slices.md#484-views-and-uniqslice)
- Further dynamic collection APIs. [§4.9](../04-arrays-indexing-and-slices.md#49-dynamic-collections)
- Cursor Contracts (`Cursor`, `UniqCursor`) and their adapters `Kimi.Iteration.shared`/`uniq` (`Shared<C>`, `Uniq<C>`); enumeration uses LendingIterator, Iterator and the `owning`/`borrowing` adapters. [§22.2](../22-core-execution-and-foreign-functions.md#222-iteration), [§4.7.1](../04-arrays-indexing-and-slices.md#471-index-expression-resolution)

## D.2. Declarations, functions and generics

- Extension declarations and extension identity; Contract fragments and Contract splitting. [§6.1](../06-declarations-and-containers.md#61-declaration-containers), [§6.1.2](../06-declarations-and-containers.md#612-container-fragments)
- Abstract functions, virtual Properties and accessors, function-own generic virtuals, `final`/newslot, bound base-function values. [§6.4](../06-declarations-and-containers.md#64-virtual-members-and-overrides)
- General Attribute semantics beyond Layout, LibraryImport and Test. [§6.7](../06-declarations-and-containers.md#67-attributes)
- Callable extensions: Borrowed, Exclusive and Consuming erased callable Types; opaque returns; receiver-dependent or lending public results; non-escaping declarations; capture aliases, initializers and parts; generic receiver Semantics and `{environment}`; general higher-ranked Callable contracts. [§3.2.1](../03-types-and-values.md#321-callable-value-types), [§7.7.2](../07-functions-and-callable-values.md#772-captures), [§15.8.2](../15-ownership-and-lifetime-analysis.md#1582-closure-dependencies), [§15.8.3](../15-ownership-and-lifetime-analysis.md#1583-escape-and-retention)
- Contract-owned Origin and length parameters, default implementations, external conformance, qualified requirement calls. [§8.4](../08-generics-constraints-and-contracts.md#84-contracts)
- Associated-Type inference beyond bounded declaration results; stronger symbolic Constraint reasoning, including decomposition (injectivity) of identity premises. [§8.2](../08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions), [§8.5](../08-generics-constraints-and-contracts.md#85-associated-types), [§8.3](../08-generics-constraints-and-contracts.md#83-constraint-proof)
- Value arguments beyond function lengths, constant Type arguments, standalone Semantics slots, partial, default and variadic generic arguments. [§4.4](../04-arrays-indexing-and-slices.md#44-function-length-parameters), [§8.1](../08-generics-constraints-and-contracts.md#81-generic-parameters), [§12.5.2](../12-expressions.md#1252-invocation-and-generic-application)
- Partial or conditional explicit specialization, specialization priorities, generic Container specialization. [§8.13](../08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization)
- Overload ranking of a direct match against callable erasure. [§10.7](../10-overload-resolution-and-inference.md#107-callable-signature-compatibility)
- Release comparison of ObjectCallCompatible status against a previously published Package version. [§12.7](../12-expressions.md#127-object-receiver-compatibility)
- Transparent source Type aliases. [§18.2](../18-modules-and-dependencies.md#182-aliases)
- Composition Root Entry/Provider declarations and selection, including test composition. [§13.8](../13-operators-and-assignment.md#138-reserved-syntax-and-the-composition-root)

## D.3. Enums, Patterns and failure

- Struct, named-payload, Type, OR, Range, Rest and Array Patterns. [§14.8.1](../14-control-flow.md#1481-patterns)
- Guard candidate capture syntax; Pattern-local acquisition selectors such as a per-binding `@move` or `@ref`. [§14.8.3](../14-control-flow.md#1483-guards), [§15.1.6](../15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)
- Refutable Patterns in further binding constructs. [§14.8](../14-control-flow.md#148-match)
- Public non-exhaustive enums, empty enums, enum representation and ABI guarantees. [§6.5.1](../06-declarations-and-containers.md#651-cases-and-payloads)
- User-defined try support and try blocks. [§17.3](../17-failure-handling.md#173-try-propagation)

## D.4. Ownership, lifetimes and effects

- Additional dynamic Move Paths. [§15.1.3](../15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)
- Non-static erased views (existential object views hiding non-static payload dependencies), static-Place Origins, and higher-ranked Origins beyond Callable direct-input quantification and Origin-parameterized associated Types.
- Destruction lifetime relaxation. [§15.6.6](../15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking)
- Effect bounds on non-virtual concrete functions, Property requirements, Function Types and the `drop` of open structures (so that erased destruction can have known effects); effect vocabularies beyond `confined` and `preserves results`; general effect systems. [§8.11](../08-generics-constraints-and-contracts.md#811-effect-bounds), [§16.3.3](../16-scope-exit-and-destruction.md#1633-handles-and-object-release)

## D.5. Concurrency

- Source threads, detached work, atomic ordering APIs, further thread-transfer and shared-access capabilities, concurrent foreign reentry. [§22.3.6](../22-core-execution-and-foreign-functions.md#2236-structured-worker-offload), [§22.3.7](../22-core-execution-and-foreign-functions.md#2237-data-races), [§15.2.4](../15-ownership-and-lifetime-analysis.md#1524-transferable)
- Per-thread I/O executors, cross-thread channels and a required I/O API. [§22.3](../22-core-execution-and-foreign-functions.md#223-asynchronous-tasks), [§22.3.6](../22-core-execution-and-foreign-functions.md#2236-structured-worker-offload)
- Atomic whole-value updates across threads; synchronized `arc` payload mutation. [§15.7](../15-ownership-and-lifetime-analysis.md#157-whole-value-updates), [§13.6.2](../13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)
- Offloading blocking foreign calls. [§22.3.6](../22-core-execution-and-foreign-functions.md#2236-structured-worker-offload)

## D.6. Unsafe code, foreign functions and runtime

- Unsafe Function Types and unsafe or foreign function values; effect declarations for foreign functions; raw pointers in concurrent execution; `tryAllocate`; strings formed from raw bytes; covariant owning containers. [§5](../05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)
- C aggregate passing, export, callbacks and varargs. [§22.6](../22-core-execution-and-foreign-functions.md#226-foreign-function-imports)
- Structure layout modes beyond Kimigayo and C. [§6.7.1](../06-declarations-and-containers.md#671-layout), [§21.1.2](../../impl/21-layout-runtime-and-code-generation.md#2112-layout-modes)
- Over-aligned allocation, arbitrary exit codes, floating-point environment control. [§22.5](../22-core-execution-and-foreign-functions.md#225-abort-locations-and-codes)

## D.7. Packages, Mods and tests

- Private-source binary distribution and a stable external Kimigayo ABI. [§18.5](../18-modules-and-dependencies.md#185-source-packages)
- Re-exports. [§18.3](../18-modules-and-dependencies.md#183-name-reachability-across-kotonoha)
- Network registries, version ranges, dependency-edge configuration overrides, Package-to-Project override. [§18.4](../18-modules-and-dependencies.md#184-dependencies)
- Mods (source generation), including their host interface, marker registration and configuration. [§6.1.2](../06-declarations-and-containers.md#612-container-fragments)
- Parameterized tests, Suite Attributes, skips, resource locks, Option and Result verification helpers, string and collection diffs, attachments, expected Abort, compile-failure tests, coverage. [§6.7.2](../06-declarations-and-containers.md#672-test-definitions), [§17.6](../17-failure-handling.md#176-test-verification-operations)
