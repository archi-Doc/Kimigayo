# Appendix D. Deferred feature index

[Specification index](../../SPEC.md)

This index links to design boundaries owned by the language sections. It adds no syntax or permissions. Implementation coverage is recorded separately in [STATUS.md](../../STATUS.md). Language-version selection is specified in [§20.5](../20-compilation-configuration.md#205-language-version-selection).

Rows follow the order of their owning chapters.

| Feature | Status | Owning section |
| --- | --- | --- |
| Weak extensions: direct Weak view conversion, liveness-only tests, unsafe weak pointers, unowned references | Not introduced | [Weak values](../03-types-and-values.md#322-weak-reference-values), [Weak operations](../13-operators-and-assignment.md#1359-weak-reference-operations), [object profile](../21-layout-runtime-and-code-generation.md#2123-windows-x64-object-and-weak-profile) |
| Borrowed/Exclusive/Consuming erased callable Types, opaque returns, receiver-dependent public results | Deferred design | [Callable Types](../03-types-and-values.md#321-callable-value-types), [Closure lifetimes](../15-ownership-and-lifetime-analysis.md#1582-closure-dependencies-and-call-results) |
| Implicit or additional ordinary base conversions; consuming, generic or Type-function runtime requirements | Deferred design | [Object views](../03-types-and-values.md#335-object-views-and-identity), [runtime Contracts](../08-generics-constraints-and-contracts.md#85-runtime-contracts) |
| Fixed-array fill/repetition and element-generator construction | Deferred design; whole initialized inputs/results and element literals remain available | [Fixed-array initialization](../04-arrays-indexing-and-slices.md#43-initialization-and-inference) |
| Mutable or exclusive-element Slice | Not introduced; mutate through authorized whole-array access and indices | [Sequence operations](../04-arrays-indexing-and-slices.md#45-operations-and-ownership) |
| String indexing and library indexers | Partially specified | [Indexing and slicing](../04-arrays-indexing-and-slices.md#46-indexing-and-slicing) |
| Dynamic collection and Slice storage ABI; additional collection APIs | Mutation, capacity, Loans/effects and complexity are specified in §4.7; concrete collection storage and the extensions listed there remain design work. Value-borrow, callable and metadata storage are specified in §21.2–21.3 | [Dynamic mutation](../04-arrays-indexing-and-slices.md#47-dynamic-collection-mutation), [runtime representations](../21-layout-runtime-and-code-generation.md#212-runtime-representations-and-metadata) |
| Raw pointer and FFI APIs | Partially specified | [Raw pointer API design boundaries](../05-raw-pointers-and-unsafe-memory.md#56-raw-pointer-api-design-boundaries) |
| Contract fragments and extension identity | Deferred design; Contract splitting and extension declarations are not introduced | [Container fragments](../06-declarations-and-containers.md#612-container-fragments) |
| Virtual/override members | Extension design; not active in this revision | [Virtual members](../06-declarations-and-containers.md#624-virtual-members-and-overrides) |
| User-defined arithmetic and general Attribute semantics | Deferred beyond the specified comparison, Layout, LibraryImport, Test and Mod marker behavior | [Operator boundaries](../13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax), [Attributes](../06-declarations-and-containers.md#65-attributes) |
| Non-escaping declarations, extended capture syntax, concurrency capabilities, general higher-ranked Callable contracts | Deferred design | [Capture rules](../07-functions-and-callable-values.md#762-capture-acquisition-and-environment), [escape](../15-ownership-and-lifetime-analysis.md#1583-escape-and-retention) |
| Contract-owned generic parameters, default implementations, external conformance, qualified requirement calls | Not introduced; inherited outer environments are defined in §6.1.3 | [Contracts](../08-generics-constraints-and-contracts.md#84-static-contracts) |
| Associated-Type inference beyond explicit identity facts, arbitrary complete-Type bindings, stronger symbolic Constraint reasoning | Not introduced | [Associated Types](../08-generics-constraints-and-contracts.md#843-associated-types), [proof boundaries](../08-generics-constraints-and-contracts.md#87-constraint-proof-system) |
| Runtime Contract Views: designation, View associated-Type bindings, Contract/exact tests and checked casts | Extension design; outside this revision's static Contracts | [Runtime Contracts](../08-generics-constraints-and-contracts.md#85-runtime-contracts), [tests and casts](../13-operators-and-assignment.md#1362-general-view-tests-and-checked-casts) |
| Const/value arguments beyond function lengths, standalone Semantics slots, partial/default/variadic generic arguments | Not introduced | [Function length parameters](../04-arrays-indexing-and-slices.md#44-function-length-parameters), [generic Type parameters](../08-generics-constraints-and-contracts.md#81-generic-type-parameters) |
| Partial/conditional explicit specialization, specialization priorities, generic Container specialization | Not introduced | [Full specialization](../08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization) |
| Direct-match versus callable-erasure overload ranking | Deferred design | [Callable compatibility](../10-overload-resolution-and-inference.md#107-callable-signature-compatibility) |
| ObjectCallCompatible implementation and release comparison | Inference, publication and call rules remain specified; implementation awaits further instructions | [ObjectCallCompatible plan](#objectcallcompatible), [object calls](../12-expressions.md#1244-object-receiver-compatibility) |
| String concatenation and string compound-assignment ownership | Deferred to a common operator model; executable acceptance requires defined acquisition, Loan, result-ownership and failure rules | [Arithmetic operators](../13-operators-and-assignment.md#133-arithmetic-bitwise-and-shift-operators) |
| Recoverable object creation, allocator selection, general value duplication | Deferred; the object and Weak public operations are specified | [Object ownership operations](../13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing), [explicit duplication](../03-types-and-values.md#351-copy-capability-and-explicit-duplication) |
| Composition Root Entry/Provider declarations and selection | Unsettled; the withdrawn design grants no syntax and no `Kimi.Console.writeLine` redirection. The built-in abort and test verification operations are specified independently | [Extension boundaries and reserved syntax](../13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax) |
| Additional enum and Pattern forms | Deferred design | [D.1](#d1-enum-and-pattern-extensions) |
| Additional dynamic Move Paths | Deferred design | [Move Paths and Partial Move](../15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move) |
| Contract-owned abstract Origins, non-static erased views, static-Place Origins, lending iterators | Deferred design; static Contracts inherit outer Origins (§6.1.3); ordinary retained storage is defined in §15.4 | [Abstract Origins](../15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations), [lifetime design boundaries](../15-ownership-and-lifetime-analysis.md#159-lifetime-design-boundaries) |
| Destruction lifetime relaxation | Deferred design | [Destruction lifetime checking](../15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking) |
| Inter-thread atomicity of whole-value updates | No guarantee; the Kimi APIs and sequential behavior are specified | [Whole-value updates](../15-ownership-and-lifetime-analysis.md#157-whole-value-updates) |
| User-defined try support and try blocks | Not introduced; built-in Option/Result try is specified | [Try propagation](../17-failure-handling.md#1724-try-propagation) |
| Source transparent Type aliases | Not introduced; a source alias opens a Container only | [Alias boundary](../18-modules-and-dependencies.md#181-external-references-and-aliases) |
| Re-export syntax | Deferred design | [Re-exports](../18-modules-and-dependencies.md#182-re-exports) |
| Private-source binary distribution and a stable external Kimigayo ABI | Deferred; source packages, semantic records and local publication are specified | [Source artifacts and binary interfaces](../18-modules-and-dependencies.md#183-source-artifacts-and-binary-interfaces) |
| Network registry, version ranges, dependency-edge configuration overrides, Package-to-Project override | Not introduced; exact-version local dependencies and graph diagnostics are specified | [Dependency configuration](../18-modules-and-dependencies.md#184-dependency-configuration-and-resolution) |
| Notification- or USN-based skipping of mutable-source reads | Deferred pending proof of complete observation and snapshot synchronization | [D.3](#d3-input-monitoring-and-reuse) |
| Mods (source generation): host interface and configuration | Deferred. Query, marker-registration and context APIs, packaging, project configuration and cache formats are undefined; until specified, no Mod is registered or executed. The execution and semantic rules remain specified for that host | [Mods](../20-compilation-configuration.md#207-mods-source-generation) |
| Automatic toolchain installation, debug information, cross-module/DLL ABI, additional CPU/OS profiles | Deferred beyond the Windows profile; explicit build and run commands are defined in §20.8.6 | [Native build](../20-compilation-configuration.md#208-llvm-output-native-build-and-execution), [LLVM profile](../21-layout-runtime-and-code-generation.md#215-llvm-windows-x64-profile) |
| Testing extensions | Named profiles and further extensions remain deferred; the initial public execution profile is specified | [D.4](#d4-testing-extensions) |
| Struct layout modes | Kimigayo and C modes are specified; special layouts are deferred | [Structure layout and ABI](../21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi) |
| Generic code sharing: baseline sharing classes, shared operations and Type policies, GenericContext and metadata-driven entries, entry/context connections, optional specialization budgets and code merging, generic scratch storage and fixed frames | Deferred; the initial profile monomorphizes. Sharing is the required future generation method, and its settled design is retained in the owning sections | [Generation policy](../21-layout-runtime-and-code-generation.md#2131-policy-and-sharing-conditions), [shared operations](../21-layout-runtime-and-code-generation.md#2133-shared-operations-and-type-policies), [fixed frames](../21-layout-runtime-and-code-generation.md#2146-generic-scratch-storage-and-fixed-frames) |
| Generic budget defaults, setting names, reporting and physical internal argument positions | Compiler choices within the adopted generation contracts and initial selection guidance | [Generic entries](../21-layout-runtime-and-code-generation.md#2136-entry-abi-and-call-responsibility), [budget strategy](B-reference-models.md#b73-two-growth-limits-and-deterministic-selection) |
| Persistent generation choices and object-code caches | Deferred until measured need and complete generation-key validation; semantic plans and independent native input summaries may be cached | [Persistence](../21-layout-runtime-and-code-generation.md#21342-persistence-and-composition) |
| Local automatic-specialization hints | Deferred until measured local needs cannot be met by the global multiplier; any future hint must be safe to ignore and must preserve semantics and resource limits | [Generation limits](../21-layout-runtime-and-code-generation.md#2135-generation-limits-and-code-merging) |
| Storage for unknown generic substitutions | Deferred; layout computation, dynamic stack/heap storage, limits, failure and effect order must be defined together first | [Fixed frames](../21-layout-runtime-and-code-generation.md#2146-generic-scratch-storage-and-fixed-frames) |
| Stack exhaustion detection, diagnostics and recovery | Unspecified; no guaranteed conversion to Abort and no recovery contract | [Storage and unwind information](../21-layout-runtime-and-code-generation.md#2155-storage-attributes-and-unwind-information) |
| C aggregate passing, export, callbacks and varargs; Unicode console adapter; over-aligned allocation; arbitrary exit codes; floating-point environment control | Deferred extensions | [Foreign functions](../22-core-execution-and-foreign-functions.md#223-foreign-function-imports), [Windows runtime](../22-core-execution-and-foreign-functions.md#225-initial-windows-runtime) |
| Concurrency, memory model and thread-transfer capabilities | Deferred design | [D.2](#d2-concurrency-memory-model-and-thread-transfer) |

## D.1. Enum and Pattern extensions

The initial [enum](../06-declarations-and-containers.md#63-enums) and [match](../14-control-flow.md#148-match-expressions-and-patterns) rules do not introduce the following extensions. Future designs must preserve these boundaries:

| Extension | Required design |
| --- | --- |
| Struct Pattern | Distinguish Fields from computed operations and respect private storage and Move permissions. Getter-based decomposition needs explicit evaluation, effect and coverage rules; it is not inverse constructor execution. Any future spelling must be distinguishable from Origin braces. |
| Named payload | Define stable element names and declaration order without changing positional payload meaning. |
| Type Pattern | Share the runtime `is`/Effective Type rules, Type identity and Origin preservation; define Pattern binding and coverage separately. Do not infer exhaustive open hierarchies from known subclasses or require hidden dynamic metadata on value borrows. |
| OR Pattern | Agree on binding names, Types, mutability and acquisition across alternatives; define the guard evaluation count. |
| Range / Rest / Array | Extend coverage explicitly; Rest lengths and dynamic indices do not become implicit Move Paths. |
| Structural Patterns on `uniq` candidates / exclusive decomposition | Define syntax, shared/exclusive Reborrow, overlapping Loans and invalidation by Case replacement. |
| Public non-exhaustive enum | Require an explicit declaration and client catch-all rules; do not silently add unknown Cases to closed enums. |
| Other binding constructs | Specify permitted refutability, failure control flow and scopes for each construct. |
| Guard candidate capture | Define explicit read-value acquisition syntax and timing and Origin/Loan escape checks; no `@copy` form exists. |
| Pattern binding acquisition selection | Define how to borrow a Copy-capable payload from its original Place, including syntax, result Type, Origins/Loans and acquisition timing. No such selector exists; this is distinct from guard candidate capture. |
| Shared Iterable requirement for user Types | Define a Contract through which a user Type yields shared element references when iterated as a bare Place (§14.6.2), including its receiver, element Type and Origin rules. Until then, user Types offer shared iteration through a member that returns an Iterable view such as a Slice. |

Object-Semantics enum construction and matching, empty enums, and representation/ABI guarantees also remain outside the [initial enum rules](../06-declarations-and-containers.md#631-cases-and-payloads).

## D.2. Concurrency, memory model, and thread transfer

**Deferred design.** Source threads and tasks, a language memory model, data-race rules, atomic ordering operations, thread-transfer and shared-access capabilities analogous to Send/Sync, and cross-thread static initialization are not specified. The initial execution model is single-threaded (§22.2).

`arc` guarantees only its atomic reference-count protocols and the runtime initialization and release ordering of §21.2.3.3. It does not authorize thread-safe access to, publication of, destruction of or transfer of source payloads. `Owned` proves lifetime independence, not thread safety. Future concurrency capability requirements may reject programs or foreign integrations that a pre-alpha compiler accepted; neither `arc` nor `Owned` preauthorizes them.

## D.3. Input monitoring and reuse

Mutable input is fixed from its actual bytes (§18.5.2); equality of file ID, size and timestamp is not content evidence. Notification cookies and per-file USNs do not yet replace reading the input. Before adopting such a path, validate the initial full scan, notification completeness and order, rename and alias updates, open writing handles, and races between synchronization and snapshot capture. Overflow, watcher disconnection, cookie failure or a journal-generation change must cause a full reread. Cookie delivery or a last-recorded USN alone is not a portable proof that nothing changed. This deferred optimization does not weaken the separate assumption that only the compiler writes registered user-cache content (§18.6.4).

## D.4. Testing extensions

The basic language and execution rules are specified in §6.5.1, §17.5, §18.8, §20.9, §21.3.7 and §22.6. The [test execution profile](../testing-profile.md) defines the adopted public interfaces and bounds. Profile defaults, temporary-directory access, solution execution and reporting are no longer deferred. Named execution profiles remain deferred; projects have one Test settings record with CLI overrides.
Parameterized tests, Suite Attributes, skips, resource locks, verification helpers for Option/Result, string and collection diffs, attachments, expected Abort, compile-failure tests and coverage are not introduced. Initially, Abort is a failure. Future cases must be separately identified, reported, selected and scheduled; do not implicitly form a Cartesian product of input lists or duplicate Non-Copy values. Define value generation, Move/process transport and static discovery before adopting parameterization. Existing `group`/`rootgroup` declarations provide organization. A future Suite must distinguish display, tags and settings inheritance, within-Suite serialization and cross-Suite resource exclusion; it must not default to shared mutable fixtures.

Reusing a child process changes static/FFI state and initialization counts, so it requires an explicit future isolation mode rather than a transparent optimization. Same-process parallel tests require a language concurrency and memory model (D.2). Entry/Provider-based test composition belongs to the unsettled Composition Root extension; it is not a prerequisite for the initial dedicated test host.

<a id="objectcallcompatible"></a>

## D.5. Deferred implementation plans

### D.5.1. ObjectCallCompatible

The implementation of the following work has been planned but intentionally not started. It begins only when explicitly instructed; until then, wait for further instructions.

The [inference and call rules](../12-expressions.md#1244-object-receiver-compatibility) and [public summary requirements](../18-modules-and-dependencies.md#187-verified-information-and-reuse) include the complete Sealed payload exception. Full public-effect inference and release checking proceed in three stages:

1. **Document the plan (current stage).** Preserve the existing specification; make no implementation changes.
2. **Implement inference and publication.** Verify bodies, callees and all explicit specializations; publish completed `Proven`/`NotProven` results in public API information, and enforce the existing call rules.
3. **Check release compatibility.** Compare corresponding public APIs with a previously published Package version and report a change from `Proven` to `NotProven` as breaking. Baseline selection, API matching and release enforcement remain to be specified.

This plan adds no Attribute and no change to compatibility rules. Stages 2 and 3 await further instructions.
