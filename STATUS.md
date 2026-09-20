# Kimigayo Implementation Status

The **2026-09-20 parameter-name/default revision** is implemented across parsing,
source round trips, Binding, Contract/foreign header validation, library shape
checks and supported generation. External name permission (`IsNameOptional`) is
independent of default-expression presence. Calls reject positional arguments
after named arguments and never skip name-required parameters. Function values
retain positional-only calling; supported specializations inherit the original
call contract. Standard library declarations, examples and fixtures are migrated.

Existing scalar default execution also works through generic entries and shared
forwarding, including supported closed Type specializations. Native coverage
includes prepared i32 defaults, concrete/shared callees and receiver positions.
General effectful, owned or borrow-producing defaults and foreign execution retain
their existing limitations; name/default syntax does not bypass those checks.

Debug/Release builds have zero warnings/errors and each passes **10,822 managed
tests** (10,814 at that checkpoint, plus the 8 rows of the later unsafe
value-position rejection). Twelve new fixtures pass **24 O0/O2 executions per configuration**, and
the Release Milestone 11 CLI suite passes 55 checks. Warm Binding, ownership and IR
writing allocate zero for the measured named-default workload. Detailed scope,
failures/fixes and commands are in [the implementation record](PLAN_HISTORY.md#parameter-names-implementation-20260920).

The **2026-09-20 call borrow reservation revision** implements call-local preparation
and simultaneous activation for implicit and direct explicit exclusive borrows.
Supported paths include ordinary/method/constructor calls, concrete and common
callable values, existing generic borrow parameters, whole-value intrinsics,
`obj`-backed `objref`/`objuniq` and complete Sealed payload projections. Reserved
inputs permit temporary shared inspection, including scalar field reads by defaults.
Existing active parent Loans, stored/control-flow-result boundaries, retained
arguments, disjoint paths and transfer cleanup remain checked. Preparation and
activation conflicts have distinct diagnostics. Reservation state adds no runtime
allocation or locking; compiler lists and scratch arrays are reused.

This does not complete dynamic collection execution, the generic by-value reference
storage ABI, arbitrary effectful or borrow-producing defaults, inherited object
views, rc/arc/Weak, or general effect summaries. Unsupported generation still fails;
these limits do not narrow §15.6.7. Debug/Release each pass 10,745 tests, including 69 focused cases; 37 fixtures pass 74 O0/O2 native executions. Warm ownership analysis and IR output allocate zero in the measured test. Exact verification is recorded in
[the reservation implementation record](PLAN_HISTORY.md#call-reservations-20260920).

The preceding Origin integration and review checkpoints follow.

The **2026-09-20 Origin syntax and elision integration** adopts `{...}` declarations
and aggregate arguments, prefix borrow annotations (`uniq{borrow}/Writer<T>{target}`),
and independent anonymous aggregate input Origins. Constructor/accessor lists,
stored-accessor inheritance, supported specialization inheritance, conditional
reconstructed borrow slots, Owned-aware result defaults and complete-contract
comparison are connected to the existing binder. Old Origin syntax is rejected;
the library, specification examples, milestone sources and affected tests use the
new syntax. The approved proposal itself is unchanged.

Origin-bearing input fields now use substituted storage metadata in ownership and
generation. Rebinding refreshes that metadata without losing anonymous binder
identity. Single nongeneric receiverless function references checked against an
expected Function Type now validate inputs, results and Origin bindings; unsupported
selection shapes are diagnosed rather than accepted with an unchecked signature.

A follow-up review of the surrounding code corrected five defects: a grouped
Container suffix before a function arrow (`(Outer).Inner -> U`) was accepted as a
Function Parameter List; a failed enum case substitution published stamped, partly
filled storage metadata; anonymous aggregate input slots could be written past the
width a caller reserved; Origin requirement accumulation indexed declaration slots
by the Type's own argument count; and requirement dependents grew once per Bind for
declarations that had dropped out. Subtree Origin summaries on `BoundType`,
consumer-stamped requirement edges and a value `OriginArgument` keep warm rebinding
allocation-free while reducing cold parse/Bind allocation and time on a pinned
Origin workload. No specification rule changed.

Debug/Release solution builds have zero warnings/errors; all **10,678 tests** pass
per configuration, including 48 focused Origin revision cases. Artifact reload,
syntax round trips, invalid lifetime/contract cases and warm ownership/emission
allocation checks pass. Additional native evidence, measurements and exact commands
are in [the integration record](PLAN_HISTORY.md#origin-syntax-elision-20260920) and
[the review record](PLAN_HISTORY.md#origin-review-20260920).

This does not complete the existing general Origin-bound/principal solver,
universal Semantics-role proofs, explicit-Origin/constrained specialization
inheritance, custom-accessor execution or general function-item/Callable erasure.
Those required behaviors remain open in [PLAN OSE3/I5/I12/I18](PLAN.md#2-execution-state).
The formal specification includes them without implementation exceptions.
No NativeAOT test was run; no whole-compiler throughput improvement is claimed.

The **2026-09-20 foreign-import symbol agreement continuation** extends
`#LibraryImport` checking to the §21.5.2 final symbol table. An import that names
one of the nine kernel32 declarations the generated runtimes emit (the seven of
§22.5.6 plus the two test-runtime APIs) is accepted only through the reserved
`kernel32` supply with that declaration's exact physical signature, and never for the `noreturn` `ExitProcess` declaration
(`ConflictingRuntimeSymbol_Kd`). Declarations of one external symbol must also
agree on the requirement `Kind` that selects dllimport generation, across source
modules and including the reserved `kernel32`/`kimi_backend` kinds
(`ConflictingImportSupply_Kd`). A repeated `#LibraryImport` on one function is now an
invalid declaration instead of an unresolved one, because one declaration selects one
external symbol (§22.3.1), and an import of a reserved supply outside its catalog —
the reviewed kernel32 definition or the backend's provided symbols — is rejected
(`UnavailableReservedImport_Kd`, §20.8.2.4, §21.5.7). A recorded declaration table is checked against both
emitted runtime IR templates, so they cannot drift. The same continuation rejects an unsafe
function acquired as a value; the rejection now covers every use that is not the
callee of a direct call, rather than a list of positions
(`UnsafeFunctionValue_Kd`, SPEC 7.7). Parentheses, the member-access right side and
an explicit type-argument list still form part of the called name, so direct calls
are unchanged. A
safe function group was then accepted against a declared Function Type without a
signature check (the OSE revision above repairs the supported comparison path).
At that checkpoint, Debug/Release solution builds were
warning-free and **all 10,602 managed tests** pass per configuration. Actual
provider identity after supply resolution, direct-call lowering, linking and
native execution of imports remain unimplemented; no native fixtures were
regenerated and NativeAOT was NOT_RUN.
[Evidence](PLAN_HISTORY.md#compiler-continuation-20260920-import-symbols).

The **2026-09-20 documentation Markdown product migration** switches the product
facade to the independent limited-profile parser. Immutable nodes/items, Binding
receiver roles and parameter classification, source-mapped optional item diagnostics,
iterative HTML output and structured source/output URL resolution are connected.
Markdig is confined to comparison tests/benchmarks; compiler assembly references,
restored assets, manifests and a fresh managed publish contain no Markdig. The
published compiler starts successfully. [Product API and placement defaults](Kimi/Compiler/Documentation/README.md).

Debug/Release builds have zero warnings/errors; **1,351 documentation tests and
all 10,561 managed tests** pass per configuration. Coverage includes the retained
CommonMark/difference corpus, 320 official non-link product HTML expectations,
explicit URL component/mapping/escaping cases, Binding/publication integration,
and deep/concurrent/reentrant/cancelled output. Collection-on/off emitted IR stays
equal. Six equal-output parse + render workloads have geometric-mean time and
allocation ratios of 0.476 / 0.394 versus Markdig; render-only results and stress
allocation tradeoffs are reported separately in the [measurements](Benchmark/DocumentationMarkdown.md#dm5-product-output-and-switch-2026-09-20).
These are bounded checks, not exhaustive conformance or whole-compiler speedups.
Optional extension-like writing diagnostics and cross-comment caches are absent.
Native fixtures were not rerun in DM5; NativeAOT was not run.
[Execution evidence](PLAN_HISTORY.md#documentation-markdown-product-switch-20260920).

The **2026-09-19 compiler continuation** adds supported source-module common
generation and Library inspection, disjoint sibling Moves under inline-part
borrows, and conservative single-input returned-reference footprints. Borrow
checking reuses one converged snapshot per operation. Debug/Release solution
builds are warning-free; each full suite passes 9,220 tests and each affected
native set passes 252 O0/O2 executions. The transitive source-dependency example
and Library inspection commands are verified in both configurations. Module/native
supply records, packages, dynamic static lifecycle and general ownership remain
unfinished. [Evidence and measured limits](PLAN_HISTORY.md#compiler-continuation-20260919-113419).
No Milestone Program was implemented; NativeAOT was not run.

The **2026-09-19 program Milestone 16 implementation** passes Binding, ownership,
checked LLVM generation, LLVM verification, linking and native O0/O2 execution.
Origin-bearing enum payloads preserve external struct references through receiver
calls, owned match Subjects and outward results. Directed reference payload fitting
and canonical call-Origin intersection substitution retain the complete contracts.
Debug/Release target harnesses each pass 57 checks, including exact output, exit 0,
empty stderr and invalid-input rejection. Both solution builds are warning-free;
managed regressions and 90 Release execution checks for programs 1–15 pass.
[Evidence and support boundaries](PLAN_HISTORY.md#program16-completion).
NativeAOT NOT_RUN.

The **2026-09-18 Documentation Comments integration** implements optional source
collection, declaration association, lazy source-mapped text, Markdown rendering
and item extraction, and Binding-backed publication queries. Debug/Release builds
have zero warnings/errors; all **8,792 managed tests** pass per configuration,
including 84 documentation cases. Collected/uncollected compilation emits identical
IR, and the documented fixture passes LLVM verification and native O0/O2 execution.
[Evidence and boundaries](PLAN_HISTORY.md#documentation-comments-integration).
NativeAOT NOT_RUN.

The **2026-09-18 program Milestone 15 implementation** passes Binding, ownership,
checked LLVM generation, LLVM verification, linking and native O0/O2 execution.
Matching borrowed result Types retain both incoming Origins; existing CFG and
liveness analysis handles this program's initialization, Move repair, continue
cleanup and last-use borrow release. Debug/Release solution builds are warning-free
and all 8,708 managed tests pass per configuration. The target and variants have
exact stdout, empty stderr and exit 0; invalid variants reject before IR publication.
[Evidence and limits](PLAN_HISTORY.md#program15-completion). NativeAOT NOT_RUN.

The **2026-09-18 Kimi.Intrinsics relocation** is complete: warning-free Debug/Release test builds and 8,691 tests per configuration pass, together with 48 Release Milestone14 checks and 36 O0/O2 whole-value update executions. [Placement, coverage and limits](#kimi-library-and-whole-value-updates); [verification record](PLAN_HISTORY.md#kimi-intrinsics-placement). NativeAOT was NOT_RUN.

The milestone roadmap now contains **38 programs**: 1–21 have source files and
22–38 have planned verification scopes. Program 20 covers inference/defaults;
21 separately covers explicit specialization. Programs 2–21 use Console.writeLine;
Hello World retains its fully qualified call. See [the complete status inventory](milestones/README.md#program-status).

The **2026-09-18 restructuring audit** passes a warning-free Release compiler/test
build, 57 alias/syntax tests, 577 existing milestone harness checks and two native
O0/O2 executions of program 13. Programs 1–14 retain executable coverage after
the source spelling change. At that checkpoint programs 15–21 failed native O2
build probes; programs 15–16 are now superseded by the completions above. Programs
17–21 retain their recorded limitations; 22–38 remain uncreated. The restructuring
itself added no compiler feature.
[Audit and limits](PLAN_HISTORY.md#programs38-restructure) distinguish current
source hashes from earlier combined-program evidence. Debug, the full managed
suite and NativeAOT were not run for this restructuring.

Updated for the adopted Windows x64 test profile on **2026-09-20**. [SPEC.md](SPEC.md) and its normative chapters define required behavior; implementation restrictions below do not weaken them. [PLAN.md §2](PLAN.md#2-execution-state) owns current work states and next actions. [PLAN_HISTORY.md](PLAN_HISTORY.md) owns dated execution, verification and decision records.

Support is partial across the compiler. Parsing, final Binding, ownership/control-flow analysis, checked LLVM generation, LLVM verification and native execution are distinct stages. A declaration certificate or successfully parsed specification example does not establish runtime support. The coverage below summarizes implemented subsets and their limits, backed by the named code/tests and recorded runs; comprehensive clause conformance remains unfinished.

The earlier program-14 implementation audit established end-to-end coverage plus completed-program and related regressions: warning-free Debug/Release builds, 8,670 managed tests per configuration, 48 target/variant/rejection checks per configuration, 52 total O0/O2 native runs of programs 1–13, and 136 related O0/O2 native fixture runs. [Frozen verification audit](bin/milestone14-work-20260918/verification.json) records source/DLL identities and per-run evidence; [execution details](PLAN_HISTORY.md#program14-completion) distinguish earlier failures and superseded states. NativeAOT was NOT_RUN. This is bounded implementation evidence, not full specification conformance.

<a id="c1-coverage-summary"></a>

<a id="c3-lexical-forms-and-types"></a>

<a id="c8-specification-review-integration-2026-09-08"></a>

<a id="c9-follow-up-specification-review-items-16"></a>

<a id="c10-type-and-literal-review-items-314"></a>

<a id="c14-tokenizeparse-increment-2026-09-09"></a>

<a id="c15-property-and-move-syntax-update-2026-09-10"></a>

<a id="c32-compile-time-switch-spelling-2026-09-12"></a>

## 1. Lexing, Parsing, and Compilation Conditions

| Implemented scope | Limits / evidence |
| --- | --- |
| Optional `///` documentation: all declaration targets, fragment/source identity, directive selection, generated provenance, source mappings, Markdown/items and effective public access | Set `Compilation.CollectDocumentation` before parsing; query `Kotonoha.DocumentationSources` or `Binding.GetDocumentation`. Product `DocumentationMarkdown` uses the independent parser, iterative escaped HTML and structured link resolution; Markdig remains only in comparison tests/benchmarks. Diagnostics are separate from language errors. No CLI, LSP server, dedicated Mod query or doctest runner is added. Recovery conservatively defers associations for a source with syntax errors. Snapshot replacement/reparse rebuilds metadata; executable IR carries none. |
| Independent [limited-profile Markdown](spec/documentation-markdown.md) syntax and item candidates | `DocumentationMarkdownDocument.Parse` accepts normalized LF text or a `DocumentationComment`. Nodes and completed candidates are safe for concurrent reads; ranges preserve original UTF-16 positions. Product classification uses Binding parameter names/receiver roles. Unicode categories are pinned to 15.0.0. Cancellation and a configurable depth limit interrupt without publishing a partial result. DM5 verification is recorded above; it is not exhaustive conformance or a compiler-wide speed claim. |
| Immutable UTF-8 source snapshots, original diagnostic positions, Unicode 15.0/NFC identifiers, indentation/brackets/continuations, literal escapes and parse recovery | `SourceDocument`, `Tokenizer`, SourceEncodingTest, UnicodeIdentifierTest and ParserRegressionTest. Source validity survives diagnostic clearing and duplicate-location reports; warnings or earlier Binding errors alone do not invalidate newly parsed source. |
| Koto syntax for Types, Semantics, Origins, generic/length arguments, declarations/accessors, captures, collections, expressions and transfers; parse/write/parse and reload | FrontEndSyntax, PropertyRevisionParse, NestedTypeParse and KotonohaSerialization tests. Capture/collection syntax is broader than executable support. `$expect`/`$require` statements parse and participate in ordinary test-only binding, control flow and ownership analysis. |
| Exact numeric magnitude/spelling and target-precision floating literal fitting; metadata-preserving numeric replacement keeps diagnostic location | NumberLiteralHelper, FloatingTypes, NumberLiteral/CharLiteralParse/StringLiteralParse and NumericReplacementTest. Ordinary literals borrow source text; relocated numeric nodes materialize spelling. This does not add Mod APIs or edited-tree persistence. |
| Reached `#if` / `#switch` validation with case-sensitive condition Names and collision checks | CompileTimeConditionEvaluator, CompilationSpecificationTest, DirectiveConditionValidationTest. Excluded source and short-circuited/unselected Case validation remain distinct. Legacy `#match` and dedicated `@move` are not compatibility syntax. |
| Unavailable `virtual`, `override`, `abstract` headers diagnose `UnavailableFeature_Kd` and recover at the next declaration/accessor | Ordinary Names with those spellings remain legal. Merged containers retain the first fragment's CodeContext; members/later fragments retain their own locations (T1a/T3a). |

<a id="c4-declarations-and-compile-time-directives"></a>

<a id="c5-properties"></a>

<a id="c16-initial-binding-pipeline-2026-09-10"></a>

<a id="c17-complete-type-representation-and-declaration-origins-2026-09-10"></a>

<a id="c18-constraint-proof-foundation-and-declaration-validation-2026-09-11"></a>

<a id="c19-core-intrinsic-identities-and-copy--owned-2026-09-11"></a>

<a id="c20-static-function-contracts-and-associated-types-2026-09-11"></a>

<a id="c21-property-conditional-conformance-and-inherited-member-binding-2026-09-11"></a>

<a id="c26-enum-case-construction-and-coreoptionresult-2026-09-11"></a>

<a id="c28-positional-pattern-binding-and-match-coverage-2026-09-11"></a>

<a id="c33-runtime-type-test-binding-2026-09-12"></a>

## 2. Binding, Types, and Kimi

`Compilation.Bind` stores Types, Symbols, selected calls and certificates on Koto with reusable tables; there is no separate Bound Tree. Final Binding invalidates old ownership state. Appending/replacing/reloading source revokes startup, conformance and Property certificates; emission requires the rebuilt current pipeline. Persistent semantic reuse remains separate.

### Names, aliases and declaration environments

| Implemented scope | Remaining limits |
| --- | --- |
| Separate Type/Value lookup, source-local scopes, forward calls, access domains, positional/named arguments and ordinary inference. Type arity selection stops at the committed lookup stage; wrong nearer arities do not fall back. | Full candidate equivalence, all inference/access/fragment clauses and general Origin proofs remain incomplete. Dual-namespace explicit arguments across overloads with different slot kinds remain Unsupported. |
| Mandatory Kimi default alias plus configured additions at one stage; explicit aliases keep their earlier stage. Named/opening aliases preserve bound Identity, root lookup and document/module environments, validate unused conflicts and diagnose distinct-binding ambiguity. | Named aliases are Container/library qualifiers, not transparent Type aliases. Packing/content IDs and persistent reuse do not become supported through alias integration. |
| Recursive group/struct/enum/Contract declarations inside groups/structs, merged fragments, nearest Self and inherited-name/access checks. Bound references retain unused enclosing Type/Semantics/Origin slots, own arity and binder identity through groups/bases. | Enum, Contract, executable and conditional implementation bodies reject nested containers; rootgroup is source-root-only. These are placement rules, not implementation exceptions. |
| Generic outer paths, intermediate/trailing Origin mappings, nested construction/Cases, fully bound aliases, bound marker/function Contracts, generic/conditional conformance lookup and enclosing input/access/borrow validation. | Bound Contracts with associated requirements or refinement ancestors are rejected. Full bound requirement identity, conditional refinement-path merging, inherited conformance composition and declaration-path cycle checks remain incomplete. Abstract Origin-bound proof limitations also apply. |
| Same-name Type arities keep independent symbols; matching struct fragments validate kind/modifier/access/base/Origin headers and source-local aliases. Inherited accessible Value-role Names cannot be redeclared regardless of signature or conditional premises. | Logical storage order, generated-fragment finalization and proof-dependent artifact coverage need completion. Container cache/rebind tests do not establish every A.20 outer-edit invalidation requirement. |

ContainerNestingTest covers declaration/rebind/serialization and 64-level nesting. [ContainerNesting](examples/ContainerNesting/README.md) executes nested Types, a generic group helper, bound named alias and static Contract implementation. This establishes those paths only; the broader requirements of §6.1.1/§8.4.9/§9.6.1/§22.2.4 remain normative.

### Types, constraints, certificates and access

| Implemented scope | Remaining limits / relevant tests |
| --- | --- |
| Nested complete Types/Semantics, owner normalization, Tuples/functions/fixed arrays, generic Type/pair slots and declared/input/static/intersection Origins. Original pair grouping preserves WholeType; explicit pair borrow annotations bind when safe-borrow and target-role evidence is available. | General bound/Loan/Origin solver, arbitrary SemanticsTarget application and universal role proofs remain incomplete. Unproved pair annotations keep definition obligations (PairGroupingBindingTest, PairAnnotationBindingTest, OriginSyntaxRevisionTest). |
| Four-valued Proven/Refuted/Unknown/Error proofs, declaration assumptions, associated Types, conditional conformance and inherited witnesses. Unknown/Error do not certify success. | Full generic-body/effect proofs, candidate equivalence and bound associated/refinement composition remain incomplete. Missing-name cause suppression retains failed certificates and independent errors (T5a/G9). |
| Late input/projection/qualifier/conformance validity is rechecked before publishing function, Property, enum, runtime-test and conformance certificates; replacement/rebind revokes stale facts. | ExpressionInputFormation, ExpressionProjectionCertificate, SignatureProjectionCertificate, AggregateProjectionCertificate, AssociatedProjectionCertificate, ConditionalProjectionCertificate, ConstraintProjectionCertificate and LateConstraintEnvironmentBinding tests cover the stated semantic paths, not general runtime execution. |
| Projection domains are retained through normalized function/accessor signatures, generic premises, associated specifications, enum payloads and bases. Conformance access checks use effective Type/Contract intersections and ancestor paths; conditional public members have their own domains. | Source-local functions are not exported and named function result Types are not inferred. Private initializer/helper projections do not themselves become API components. ApiAccess, PropertyApiAccess, ProjectionApiAccess, ConditionalPremiseAccess and ConditionalMemberAccess families cover rejection/recovery. |
| Provisional unresolved conditions remain pending, including capability queries through compound Types; associated/refinement premises keep prerequisite provenance. Available independent paths remain usable, cycles/unfinished evidence cannot succeed. | DeepRefinementPremiseBindingTest covers 32/64-level chains/diamonds; ProvisionalContractPremiseBindingTest and UnresolvedCapabilityProofBindingTest cover pending/final transitions. Complete public effects remain unimplemented. |
| Generic signature/call input constraints validate normalized Types and erased projection qualifiers, owners, bases, associated definitions and runtime-test operands before/after late validation. Pattern Type validity precedes coverage publication. | Generic signature tests establish certificate safety. General universal generic execution is only the subset in §4. Invalid declarations remain readable for recovery; malformed computed/Contract Properties without getters retain parser diagnostics instead of crashing. |
| Layout Attributes validate struct targets, literal C/Kimigayo mode, duplicate/conflicting fragments and one selected C-field fragment. Selected unknown/wrong-target Attributes invalidate declarations/certificates; excluded markers stay excluded. | Source Attribute validation is not complete C layout/FFI execution. Generated-fragment provenance, open/derived/zero-size C layouts and general imported native signatures need separate verification. No Mod marker API is introduced. |
| Eligible immutable integer locals/static stored Properties supply checked fixed-array lengths, including qualified/access-checked constants, 128-bit intermediates and nonnegative-isize bounds. | Calls, var, parameters, instance Fields, custom/computed getters, cycles and inaccessible constants cannot supply these constants. No getter execution or lazy static initialization is implied. |
| Local fixed-array `_` element inference and candidate-local fixed-shape argument fitting; grouped arrays/Tuples/pointers/functions/Origins remain Type arguments. Selected call plans retain separate length/Type substitutions, explicit/inferred lengths and checked formation expressions. | Empty untyped arrays/conflicting shapes reject; general collection/control-flow inference, mixed nested tuple/array default Binding, and all generic length cases remain incomplete. Tests include ConstantLengthBinding, GenericTypeArgumentBinding, array inference and length-call families. |
| Tuple and fixed-array call literals retain candidate-local numeric fitting and nested Type/length/Origin evidence. Established input Types precede result expectations and numeric defaults; overload ambiguity, argument order and winner-only acquisition are preserved. Shared literal-aggregate arguments materialize once. | General contextual nested-call inference and shared generic aggregate execution remain incomplete. `AggregateArgumentInferenceTest` verifies this bounded Binding and concrete execution slice. |

### Kimi library and whole-value updates

The public ownership functions now live in `Kimi.Intrinsics`. `Kimi.replace`,
`Kimi.exchange`, `Kimi.swap`, and `Kimi.makeObj` are no longer lookup candidates;
use `Kimi.Intrinsics.*`, `Intrinsics.*` through the default Kimi alias, or an
explicit named/opening alias of the group. Ordinary shadowing, canonical compiler
identities and rebind invalidation are preserved.

| Current declaration placement | Implemented declaration / execution boundary |
| --- | --- |
| `Kimi.Copy`, `Owned`, `Callable`, `Sealed` | Intrinsic Contract identities; existing proof rules unchanged. |
| `Kimi.Option<T>`, `Result<T,E>`, `Iterator`, `Slice<T>` | Existing enum/Contract/view declarations; bounded generation and protocol support below. |
| `Kimi.SliceIterator<T>` | Compiler-supplied concrete helper with init/next; Slice.iterate uses it. This does not finalize a general public iterator-constructor API. |
| `Kimi.Console.writeLine(text: string) -> ()` | Existing owned-string UTF-8 output. |
| `Kimi.Intrinsics.replace<T>`, `exchange<T>`, `swap<T>` | Existing whole-value updates, with the storage limits below. |
| `Kimi.Intrinsics.makeObj<T>(value: T) -> obj/T` | Existing concrete exclusive-object factory; see §4 exclusive objects. |
| Remaining `Kimi.Intrinsics` ownership family | makeRc/makeArc, strong/Weak clone, downgrade/upgrade and cyclic factories are specified but unimplemented. |

The placement/reference in [§22.1.1](spec/22-core-execution-and-foreign-functions.md#2211-declaration-placement-and-function-reference) is normative; this table describes implementation coverage. `$abort` remains a separate built-in.

Kimi declarations and ordinary bodies are embedded from `Kimi/Library/*.kimi`. Immutable source text and successfully lexed tokens are cached across compilations; syntax, source documents, symbols and binding state remain compilation-local. The explicit catalog supplies recognition identities and compiler hooks, independently of declaration order. Ordinary helper containers/members use normal indexing and binding, and directly called source bodies are collected for ownership analysis without a Slice-specific list. Private signature-only loading is restricted to registered compiler functions; user bodyless-function rules are unchanged. File-specific `compiler://Kimi/<version>/*.kimi` locations are preserved.

The catalog has **31 declaration entries, 14 validated**: Copy, Owned, Callable, writeLine, Option, Result, Sealed, replace, exchange, swap, Iterator, Slice, makeObj and Test.tempDirectory. Ownership APIs and Weak now have individual entries; the retired ObjectOwnership ID is reserved, with family completeness reported separately. Stable IDs are independent of catalog positions. Declaration validation is explicit, followed by bound-identity checks; status queries do not rerun validation. Rebinding and emission still reject changed compiler contracts. These checks retain the existing supported contract subset and do not certify complete library APIs/runtime support. Iterator/Slice and concrete makeObj retain their previous execution boundaries; rc/arc/Weak remain unimplemented. Required unfinished APIs remain PLAN G4.

`Kimi.Sealed` has intrinsic Identity and four-valued proof of the normalized outer Core, rejecting open structs, Never and non-owner Semantics without recursive field constraints. User conformance cannot manufacture evidence. `Kimi.Intrinsics.replace/exchange/swap` are ordinary parsed generic declarations with trusted compiler implementation identities, normal name resolution/shadowing/inference and textual argument order. Compiler-owned signatures need no source bodies; ordinary source functions still do.

Binding distinguishes explicit Sealed payload projection, complete payload receivers, protected base projection and nested handle/reference storage layers. Concrete obj-backed object borrows and complete Sealed payload projections have checked ownership and native lowering; object references keep the original header address and projections derive the payload address. Inherited/open views and rc/arc borrowing retain their wider runtime limitations. Generated accessor execution and full ObjectCallCompatible effects remain incomplete. SPEC.md explicitly defers ObjectCallCompatible inference/publication and release checking pending further instructions.

Ownership and native lowering support whole-value updates on complete mutable owner locals (scalars, strings and supported aggregates) and concrete scalar/struct/fixed-array uniq targets whose contents are proven Owned. Target reservations begin before later arguments and activate after preparation; replace retains original-location destruction, exchange/swap transfer old-value responsibility without user destruction. Aggregate exchange results no longer use the function result slot as an input-Origin anchor. Borrowed lowering's Owned condition is an **implementation limit**, not an added API constraint.

Storage-polymorphic updates, general field/index targets, nested handle/reference storage updates and borrowed updates without an Owned-content proof remain unfinished. [WholeValueReplacement](examples/WholeValueReplacement/README.md) covers ordinary/borrowed struct updates; the object tour and program 14 remain specification examples beyond executable coverage.

<a id="c7-control-flow-and-failure-handling"></a>

<a id="c24-whole-place-ownership-cfg-and-cleanup-plans-2026-09-11"></a>

<a id="c27-enum-construction-ownership-and-ordered-cleanup-2026-09-11"></a>

<a id="c29-owned-match-acquisition-decomposition-and-cleanup-2026-09-11"></a>

<a id="c30-current-control-flow-syntax-results-and-cleanup-2026-09-12"></a>

<a id="c31-transfer-seeded-unreachable-ownership-checking-2026-09-12"></a>

## 3. Control Flow and Ownership

The existing CFG fixed-point analysis uses checked Binding operations and distinguishes runtime reachability, structural completion and source checking. It handles whole-Place initialization, Copy/Move/partial paths, assignment histories, locals/temporaries/arguments/results, concrete enum acquisition and supported aggregate/struct responsibility. No runtime arrivals or results are invented for noncompleting source.

| Implemented scope | Remaining limits |
| --- | --- |
| if/short-circuit/while/do/loop/labels/require/match and return/yield/exit/continue/defer; result acquisition before normal cleanup and delivery only after cleanup completes | General effects/refinement and unsupported representations remain guarded. Abort/divergence produces no later ordinary cleanup or result. |
| Borrow results with the same referent Type and Semantics infer an intersection of incoming Origins. Branch-order, nested if, match and named-do result tests preserve both dependencies; checked pointer SSA transfers require semantic fitting. P15 executes branch initialization, Move/repair across normal and continue backedges, last-use Loan release and ordered cleanup at O0/O2. | This does not complete arbitrary unequal active acquisition stacks, general Origin variance/inference, deferred checking-history joins or interprocedural effects. Explicit result contracts and mismatched Types/Semantics still reject. |
| Supported scalar/Unit defaults are checked independently even when unused, supplied or bodyless requirements. Calls acquire explicit inputs first, then omitted defaults in declaration order using prepared snapshots; no inference from defaults. | General owned/borrowed/effectful defaults, recursive-default completion proof, escaping borrows/captures and generic Copy proof remain unfinished. |
| Defaults support scalar operations/conversions/selections/do, contained transfers, scalar/Unit locals and updates/finite loops, require and Copy scalar subplace reads. Match defaults read scalar candidates, acquire body bindings and preserve false-guard effects. Scalar-only tuples can be copied from prepared arguments, constructed, saved locally, destructured and read; mutable local/body bindings permit scalar-leaf updates. Prepared scalar-tuple copies include literal/local/returned/selection storage. Scalar subplace inspection recognizes acquired Copy/Move tuple/array arguments, including forwarded parameters and owned contents with checked cleanup. Generation checks exact argument slots and verified initialization/lifetimes. Definite non-Copy prepared acquisition reports `DefaultArgumentMove_Kd`. | Final default results remain scalar/Unit. General aggregate-producing default expressions, whole aggregate guard candidates and owned defaults remain unsupported. Prepared parameters and immutable/candidate bindings cannot be updated through these paths. Defaults can also read scalar fields through prepared ref/uniq arguments and receivers. General referent/getter/call/borrow/capture paths need further plans. |
| Never calls/arguments/defaults and scalar operands retain later source checking and acquired-argument histories without initializing absent results. Omitted-default completion is separate from callee-body completion. | Direct Never operand fitting has earlier Binding limits; recursive expansion stays pending and bounded. |
| Straight-line, closed acyclic and closed same-region cyclic per-target replay; initialization guarantees intersect and possible Move/assignment histories combine. Stored Loan liveness follows checking seeds/replay links. | General iteration transfer replay, unequal active Loan joins, deferred-cleanup joins and effectful divergence remain incomplete. |
| Partial-terminal selections preserve normal branch tails and pending histories; logical expressions keep evaluated/skipped paths; noncompleting conditions preserve branch checking without runtime results. Completing scopes and caught scope/selection yields retain post-cleanup normal arrivals and separate dead-source histories. | General loop-backedge proof must not be inferred from closed normal CFG replay. |
| Completing while/loop bodies and bounded scalar/Unit/string/tuple matches retain mixed histories. New implementation adds return/exit/enclosing-yield guards, Never calls, builtin Abort, state-neutral divergence and finite enum/owned-string decomposition. Abandoned guard protection ends independently on terminal histories; logical Never operands fit bool while invalid operands still fail. | Focused new cases cover Binding, ownership, generation, native transfer/Abort/divergence and cleanup. Existing regressions were not rerun. General backedges/continue guards, unequal Loan joins, deferred cleanup, effectful divergence, borrowed aggregate replay and user destructors remain incomplete. Historical 4/16/64-guard measurements apply to their earlier source state. |
| Every pending path retains its original transfer target and pre-cleanup state; local cleanup applies to normal tails. Transfers after a noncompleting condition cannot create normal arrivals. Ordinary/single-target joins restore their original region. | Missing seeds or unsupported proof paths reject; source-unreachable operations remain checked under §14.10.3. |
| State-neutral divergence and scalar/Unit loop-local effects preserve enclosing facts; a noncompleting while condition retains post-acquisition state when its body-local proof holds | Outer-mutating, owned/effectful divergent bodies and general deferred continuation behavior remain guarded. |
| String/element comparisons and arguments, supported reference storage/results, struct/array borrows and child Reborrow retain owner protection through calls/later arguments/cleanup; static disjoint paths can coexist | General effect summaries, inherited object views, arbitrary borrowed Subjects/aggregates, Origin-bound proofs and all retained dependencies remain incomplete. |
| Concrete ref/uniq tuple scalar reads and explicit/implicit borrows of stored struct, tuple and fixed-array elements use checked tuple layout and existing Loan tracking. Returned shared projections, caller temporaries, last use and owned-element cleanup are verified; escaped expression temporaries and incomplete owners reject. Immutable reference locals with one initialization preserve actual parent ancestry using reused definition tables. | Stored exclusive-reference reborrows and non-Copy element acquisition remain incomplete. `BorrowedTupleEmissionTest` and `BorrowedTupleProjectionTest` cover the bounded paths, reload and zero-allocation warm ownership/emission. See the [T4r/T6b record](PLAN_HISTORY.md#general-compiler-20260918-142715). |
| Concrete exclusive tuple scalar assignment and tuple/struct numeric compound and integer prefix/postfix updates execute with checked layout/arithmetic. Assignment evaluates RHS first; updates secure one receiver and old value before RHS, retaining a child Loan. Conflicting self-reads reject; a prior scalar Copy works. Return prevents the store; overflow Aborts without cleanup. | General shared generic field updates, disjoint borrowed-field paths and returned-exclusive ancestry through a still-live borrowed parent remain incomplete. `BorrowedFieldUpdateTest` and related regressions pass in full Debug/Release suites (8,866 each) and 206 O0/O2 executions; see the [T6a record](PLAN_HISTORY.md#general-compiler-20260918-142715). |
| Explicit reborrowing of a shared aggregate reference stored in a borrowed tuple loads that reference, rather than borrowing the slot containing it. Its original Origin is retained, so the result can outlive the wrapper while its referent remains valid. | Referent replacement while the result remains live and shared-to-exclusive escalation reject; an exclusive reborrow of a stored `uniq` reference through a `ref` base fails in Binding (T6k). Stored exclusive-reference reborrowing and general shared generic forms remain incomplete. `BorrowedTupleProjectionTest` and the [T6c record](PLAN_HISTORY.md#general-compiler-20260918-142715) retain the focused and native evidence. |
| Returned references (`relay(p) -> uniq/T from p`) trace Loan ancestry to the one acquired argument named by the public result Origin, so the suspended parent is usable after the returned Loan ends, including nested relays and immutable locals initialized from calls. | Defaults, Origin intersections, value calls and compiler functions conservatively have no traced ancestor. Parent use while the returned Loan lives rejects. `ReturnedBorrowAncestryTest`; [T6d record](PLAN_HISTORY.md#general-compiler-20260918-153130). |
| Borrowed projections apply §15.6.2 paths and permissions: distinct inline fields/Tuple elements under one root are disjoint, and shared-with-shared access never conflicts. Direct nested inline paths below a borrowed base (`p.left.value`, `p.0.1 += 2`, `p.1.value++`) read, assign and update with one validated summed offset. | Array subscripts, stored-reference referents and unknown/intersected call provenance stay conservative; nested explicit borrows (`p.inner.c@uniq`) are supported. Compound/increment updates use the updated field path as their receiver footprint. `DisjointBorrowedProjectionTest`, `NestedBorrowedProjectionTest`; [T6e–T6j records](PLAN_HISTORY.md#general-compiler-20260918-153130). |
| Explicit borrows of inline parts of owned locals and parameters (`let a = pair.left@uniq`, `o.inner.c@ref`, Tuple `t.1.0@uniq`), including implicit call-argument and `uniq` receiver borrows, borrow in place at a validated summed offset. Their Loan footprint is the static path, so sibling borrows, element reads and writes execute. Element accesses through any projection now conflict with a live exclusive Loan of their root. | T6s allows sibling Moves before/during the Loan while the borrowed subtree and its containing storage stay initialized. T6t preserves that acquired-input footprint through direct calls with a single declared input result Origin; calls conservatively retain the entire argument footprint. Overlapping/whole-owner Moves or replacement still reject; conditional Moves, nested paths, reborrows, repair and exactly-once cleanup are verified. Scalar parts can be borrowed with `@uniq`/`@ref` (T6m), but arithmetic directly on scalar references does not bind. Dynamic subscripts and generic bodies are conservative or unsupported. `OwnedPathBorrowTest`, `ScalarBorrowShorthandTest`, `SiblingMoveBorrowTest`; [T6i record](PLAN_HISTORY.md#general-compiler-20260919-082645). |
| Owner scalar temporaries used as `ref/T` arguments are materialized once and borrowed. This covers call results, `bool` results and untyped literals (`read(one())`, `read(1)`, `check(1.5)`). The temporary is a Loan root, so a returned reference used after the statement rejects. Literal borrows rank below direct literal fitting. | Generic literal inference (`inspect(1)`, `inspect(1.5)` for `ref/T`) works (T6p), and returned-Origin calls with literal arguments bind their Origin (T6q). Explicit `1@ref`, `one()@ref` and `1@uniq` materialize the temporary (T6o). There is no implicit `uniq` temporary borrow, as specified. `ScalarTemporaryBorrowTest`; [T6n record](PLAN_HISTORY.md#general-compiler-20260919-094939). |

Normal transfer cleans acquired responsibilities in the specified order; destruction follows reverse logical order. Static partial Moves track construction separately from current completeness, permit local var repair and clean only remaining initialized parts. Parameters remain immutable. Dynamic Non-Copy extraction, arbitrary paths and general user-destructor interactions retain explicit limits described below.

Evidence: OwnershipAnalysis, CurrentControlFlow, ControlFlowConformance, ReferenceEmission, StringGuardEmission, default/continuation families, and the [preceding control-flow execution record](PLAN_HISTORY.md#execution-1). The detailed target/effect/cleanup fixes and withdrawn proof approaches are historical records, not alternative current algorithms.

<a id="c6-expressions-and-operators"></a>

<a id="c12-first-executable-milestone"></a>

<a id="c23-startup-selection-and-corewriteline-binding-2026-09-11"></a>

<a id="c34-minimal-literal-output-executable-2026-09-13"></a>

<a id="c38-compiler-pipeline-and-emission-preparation-2026-09-13"></a>

<a id="c39-compiler-review-dead-code-removal-and-emission-restructuring-2026-09-13"></a>

<a id="c40-scalar-control-flow-emission-and-nativeaot-2026-09-13"></a>

<a id="c41-scalar-selection-and-loop-results-2026-09-13"></a>

<a id="c42-deferred-cleanup-execution-2026-09-13"></a>

<a id="c43-checked-i32-division-and-remainder-2026-09-13"></a>

<a id="c44-scalar-functions-return-and-explicit-main-2026-09-13"></a>

<a id="c45-i32-bitwise-operations-and-checked-shifts-2026-09-13"></a>

<a id="c46-integer-execution-through-64-bits-2026-09-13"></a>

<a id="c47-explicit-integer-conversions-2026-09-13"></a>

<a id="c48-owned-string-locals-and-conditional-cleanup-2026-09-13"></a>

<a id="c49-owned-string-control-flow-results-2026-09-13"></a>

<a id="c50-owned-string-function-parameters-and-results-2026-09-13"></a>

<a id="c51-string-comparisons-and-backend-memcmp-2026-09-13"></a>

<a id="c52-unguarded-whole-subject-match-execution-2026-09-13"></a>

<a id="c53-copy-subject-match-guards-2026-09-13"></a>

<a id="c54-shared-string-arguments-and-referent-comparison-2026-09-13"></a>

<a id="c55-string-guard-candidates-and-temporary-shared-arguments-2026-09-13"></a>

<a id="c56-whole-tuple-and-fixed-array-execution-2026-09-13"></a>

## 4. LLVM Generation Coverage

`LlvmEmitter` supports the windows-x64-v1 Application profile, implicit top-level entry or eligible `public func main() -> ()`, with final Binding/startup/ownership required. Supported nested groups/structs/enums/Contracts and bounded generic/closure/default paths are admitted. Supported source-module bodies share one final module and ABI map; all modules retain source-error, container and unused-body checks. Only the selected implicit startup wrapper is emitted. Library inspection emits internal language functions and no OS entry, with null manifest entry/subsystem; LLVM verification and COFF generation are verified. Native Application build/run reject Library inspection output. Unsupported bodies remain rejected.

| Area | Implemented generation and recorded execution | Limits |
| --- | --- | --- |
| Scalars/numerics | bool, i8/u8 through i128/u128, isize/usize, char, f32/f64, Unit; locals/results/calls/control flow; checked integer arithmetic/bitwise/shifts and compound updates; float arithmetic/comparisons | i128/u128 division/remainder prohibited by the initial profile; char arithmetic and float literal Patterns unsupported. |
| Conversions | All 12×12 integer conversions; target-precision literals; f32↔f64 and <=64-bit integer↔float; checked narrowing, truncation and same-Type owned acquisition | Typed 128-bit↔float and char/bool numeric conversions prohibited. Preserve NaN/infinities/signed zero/underflow and finite-overflow Abort. |
| Strings | Owned literal-backed handles, Move/replacement, temporary/control-flow/function results, output and six UTF-8 byte comparisons; supported ref/string forwarding and element arguments | Non-Copy even with Static backing. No general source Heap-construction, interpolation, Stringify or concatenation support. Reference storage/return and nested string-borrow forms remain representation-specific. |
| Direct functions/defaults | Recursion, named arguments, scalar/Unit/string/tuple/fixed-array/supported struct arguments/results, Never and normal/deferred result delivery; bounded scalar defaults | General default ownership, callable/borrowed ABI and owned result forwarding remain incomplete. |
| Structs/references | Explicit and eligible synthesized constructors, completion checks, owned arguments/results, fields, deinit then reverse field destruction, supported explicit borrows/input-Origin returns, borrowed receivers and field operations | Full inherited/base-layer layout/cleanup, custom/computed accessors and arbitrary borrowed aggregates remain unfinished. |
| Enums/Patterns | Concrete and finite symbolic enum storage; scalar/string/char/Unit matches; nested tuple/enum scalar/Unit guard candidates. Owned string leaves have checked decomposition, Move/body lifetime, replacement/results and remaining-payload cleanup. Nested string literal Patterns compare exact UTF-8 without owning literals; Case tests precede payload reads. P16 adds declared-Origin enum storage, supported shared struct-reference payloads and external-Origin forwarding through owned matches/results, with canonical receiver-Origin intersections and checked reference payload shortening. | Focused evidence only for the extensions. Composite string/aggregate guard candidates, borrowed Subjects/decomposition and user-destructor payloads remain incomplete. General aggregate covariance and arbitrary Origin-polymorphic ABI are not established by P16. Valid construction does not prove every Pattern executable. |
| Shared generics | Universally checked supported storage CFGs, conditional stored-field CopyOrMove, length-dependent array transfer/destruction, logical lengths, finite symbolic enums, exact fixed scratch, concrete ABI entries and deduplicated policies. Programs 13/14 add Slice handle parameters/fields, shared Copy-field reads, unique scalar-field writes, runtime element-stride borrowing, externally borrowed Option results and flat Copy enum Case matching. Closed storage descriptions and forwarded constructor/member calls preserve Origins without rebinding source bodies. Shared bodies also call ordinary concrete free functions (including Unit results) and use bool plus signed/unsigned 8–64-bit integer leaves with checked `+ - * / %`, unary `-`/`+`/`not`, comparisons and callback results (G10a). Shared bool/integer `and`/`or` and scalar result joins preserve conditional evaluation, secured results and cleanup (G10b). Owned string literals/locals/parameters/results, conditional Move/cleanup, concrete/generic forwarded string results and canonical `Console.writeLine` execute with lifetime-audited coverage (G10c). Canonical Abort/require, concrete/shared/forwarded/specialized Never calls and entries, and integer bitwise/checked shifts execute (G10d–f). Supported concrete constructors and shared/exclusive/owned receivers preserve acquisition, evaluation and cleanup order (G10g). Checked 8–64-bit integer conversions reuse the ordinary range checks, including conditional/Phi paths (G10h) | Borrowed-string operations, wider receiver layouts/defaults, floating-point/128-bit/character operations, general universal operations/effects, symbolic compound fields, arbitrary borrowed generic ABI, general symbolic payload matching, all-terminal Never matches and all optimization/resource contracts remain unfinished. Guarded/nested shared Patterns are not added. No per-instantiation source rebinding. |
| Specialization/forwarding | Closed Type specializations checked against original contracts, selected independently of overload choice; inherited name permissions and supported scalar defaults; shared direct-call adapters, indexed shared borrows without Copy on T and checked shared i32 arithmetic | Length/receiver/constrained headers, general default expressions and explicit specialization Origin binders unsupported. Concrete context expansion is bounded at depth 128; not full resource-contract coverage. |
| Common functions/erasure | Checked indirect calls with independent-result signatures and direct ref/uniq inputs, exclusive receiver reservations and shared receiver protection, scalar/Unit snapshots in an 8-byte inline environment, owned common-handle Move/return/cleanup. An acquired Copy concrete Shared environment can erase inline without consuming the original Copy value | Explicit parameter Types and supported fixed signatures required. Existing common-call result support includes scalar/Unit/Never; the new concrete path has a narrower result ABI. Existing-value erasure is bounded to Owned Copy environments of at most 8 bytes and supported scalar/Unit result ABI. Owned Non-Copy/heap erasure and general function-item erasure remain unfinished. |
| Concrete closures | Distinct environment identity, scalar/Unit/string, supported nested concrete, ref/uniq and obj captures, mutable snapshots, explicit nested returned closures, direct Shared/Exclusive/Consuming calls, borrowed exclusive receivers, reverse remaining-capture cleanup and empty environments | Expression results infer; block bodies require a result annotation. General aggregate captures and Origin-dependent capture/erasure combinations, implicit capture propagation across missing enclosing environments, generic capture effects, partial environment moves and full Origin-dependent signatures remain incomplete. A wider source form can bind yet still be rejected by ownership or generation. |
| Callable constraints | Supported scalar and per-call `ref/T` input `Callable<uniq, S>` calls through `uniq/F`, including repeated mutation and exclusive borrowed-capture environments, use a concrete compile-time entry adapter within the shared generic body. No erased common handle or per-instantiation source rebinding is introduced | Binding retains literal ref/uniq/owner contracts; executable witness coverage is narrower. Owner acquisition witnesses, general overloaded/Origin-polymorphic signatures and arbitrary callable parameters/results remain unfinished. Shared callback adapters now admit i32 as well as bool/isize/Unit results. |
| Static members | Verified immutable group integer/bool literal reads with no observable initialization effect can fold | Mutable/effectful statics, static address identity, first-access/shutdown/cycles, Origin-erased inherited-environment keys, uniform initializer/destructor certificates and shared initialize-and-address/lazy lifetime protocol are specified but unimplemented. |
| Sequences | Two-word Slice/range handles, backing Origins/Copy, scalar Copy reads and indices snapshots; full and explicit half-open Slice views check bounds before pointer arithmetic. Slice iteration and explicit indexing borrows retain shared backing dependencies; program 13 preserves its first Sample reference across later next calls and exhaustion. Views use pointer/length storage without an element buffer. Built-in ResolvedRange, shared range and supported Copy-array iteration remain implemented subsets | General Index/from-end/saved or inclusive Range/Slice APIs, collection/consuming iteration, tuple iteration bindings, general user Iterable/Iterator dispatch and arbitrary Slice element results remain incomplete. Generic Slice generation currently covers copied fields, length and explicit element borrows; not every concrete/shared form is executable. Mixed Slice provenance widens conservatively. The current suites and native sequence fixtures cover the selected negative/bounds/zero-size paths; this does not certify every Slice API. |
| Exclusive objects | Concrete makeObj acquisition, a 16-byte header, 48-byte payload ValueMetadata, 24-byte ObjectDescriptor, deterministic nonzero Type keys, obj-backed object borrow/Reborrow, complete Sealed payload projection and borrowed member calls. Exchange preserves the containing object; final cleanup destroys the dynamic payload before freeing the original allocation. obj captures can transfer through a Consuming closure | The Windows profile and existing finite payload-layout limits apply. Generic-body factories, inherited/base/Contract views, rc/arc/Weak and general refinement are not implemented. Full ObjectCallCompatible inference/publication/release stages remain deferred. |
| Collections/FFI | Collection/pointer syntax, declaration constraints and runtime `is` Binding have foundations. Binding diagnoses `#LibraryImport` argument forms, §22.3.1 declaration shape/placement, §21.5.2 reserved external names (`InvalidLibraryImport_Kd`), §22.3.2 signature Types (`UnsupportedImportSignature_Kd`), conflicting physical signatures for one external symbol across source modules (`ConflictingImportSignature_Kd`), collisions with the generated runtime's own kernel32 declarations (`ConflictingRuntimeSymbol_Kd`, shared only through the reserved kernel32 supply with an equal physical signature) and names without a requirement of the defining module for the current target (`MissingNativeRequirement_Kd`); imports still cannot complete Binding | Dynamic Array/Dictionary mutation and cost contracts and complete C/pointer/import execution are not established. Runtime Contract Views remain deferred. |

Generation validates typed inputs, exact Type identity, constants, dominance, result arrivals, live flags, Loans and cleanup plans. The Writer does not reinterpret syntax. Strings use a 24-byte handle; tuples use physical alignment order separately from logical destruction order; arrays use stride-based layout. Current aggregate layout retains depth 64 and size/count limits of int.MaxValue; PLAN G7 requires checking these against the specified resource rules. These are internal representations, not a general external ABI. Measured hot-path reuse does not establish allocation-free module preparation.

### Aggregate and element coverage

The following subsections preserve the existing numbered entry points. Detailed implementation decisions and test-by-test records moved to [the historical area snapshot](PLAN_HISTORY.md#status-area-snapshot); these summaries describe the cumulative subset, rather than repeating old blanket struct/generic/borrow exclusions.

<a id="41-aggregate-selection-results-2026-09-14"></a>

### 4.1. Aggregate Selection Results

Tuples/fixed arrays support if/do/loop/match results, local replacement, nested payloads and result transfer. Results are secured before cleanup and consumed only after the matching lifetime Join; no aggregate phi or result-specific flags are required. Abort/nonterminating cleanup prevents delivery. AggregateResultEmissionTest covers malformed plans, reload and measured warm reuse.

<a id="42-tuple-and-fixed-array-function-abi-2026-09-14"></a>

### 4.2. Tuple and Fixed-Array Function ABI

Ordinary owned tuple/array arguments and results use acquired argument/result slots, including nesting, Copy/Move, zero-size values, named calls and shallow recursion. Nonzero values pass by ptr; zero-size values retain semantic acquisition without physical storage. Arguments/results cannot alias; direct result-storage forwarding remains incomplete. Shared Loans end before result/parameter destruction under the checked plans.

<a id="43-copy-element-reads-from-tuples-and-fixed-arrays-2026-09-14"></a>

### 4.3. Copy Element Reads from Tuples and Fixed Arrays

Numeric tuple selectors and isize fixed-array indices read supported Copy elements through checked addresses, including nested/static/dynamic paths, owners, parameters and temporaries. Receiver/index evaluate once; bounds and owner lifetime remain checked. Dynamic Non-Copy acquisition is not enabled.

<a id="44-simple-assignment-to-copy-elements-of-tuples-and-fixed-arrays-2026-09-15"></a>

### 4.4. Simple Assignment to Copy Elements of Tuples and Fixed Arrays

Initialized local var tuple/array elements support Copy assignment. The RHS is secured first, then location/bounds are resolved; an exclusive Loan covers old-value destruction and placement. Nested indices and zero-size elements retain evaluation and checks. Parameter/temporary mutation and initial construction through element writes are not supported.

<a id="45-numeric-element-updates-in-tuples-and-fixed-arrays-2026-09-15"></a>

### 4.5. Numeric Element Updates in Tuples and Fixed Arrays

Numeric compound assignments and integer prefix/postfix inc/dec evaluate location once, read the old value and evaluate the RHS under the exclusive Loan. Self-reads through conflicting aliases reject; use a prior Copy where valid. Bounds/arithmetic checks and Abort/transfer cleanup remain independent of optimization. The earlier self-read allowance was corrected, not retained as valid behavior.

<a id="46-loan-disjointness-for-static-element-paths-2026-09-15"></a>

### 4.6. Loan Disjointness for Static Element Paths

Distinct tuple selectors/literal array paths permit independent operations under existing shared/exclusive Loans. Root protection remains during index evaluation; dynamic sibling paths use a conservative known prefix. Constant evaluation does not broaden the specified literal-only static Move/disjointness rule.

<a id="47-non-copy-element-replacement-in-complete-owned-values-2026-09-15"></a>

### 4.7. Non-Copy Element Replacement in Complete Owned Values

Complete initialized local var elements support replacement of strings/supported Non-Copy aggregates: secure RHS, locate under exclusive Loan, destroy exact old element, transfer responsibility and end Loan. No backup Copy/whole-parent cleanup is added. Noncompleting RHS skips location; bounds Abort does no destruction or placement. General user-deinit/retained-reference element update plans remain incomplete.

<a id="48-static-element-partial-moves-reinitialization-and-destruction-of-remaining-parts-2026-09-15"></a>

### 4.8. Static Element Partial Moves, Reinitialization, and Destruction of Remaining Parts

Constructed owned tuple/array locals support static Non-Copy Move, sibling use, local var repair and whole acquisition after all missing parts are repaired. Construction and current completeness are separate. Sparse referenced paths and untracked remainders avoid array-length expansion; remaining parts destroy in reverse logical order with flags only where conditional. Unconstructed element writes, let repair, missing-part reads and dynamic accesses that may reach missing parts reject. Dynamic Non-Copy and temporary-element Moves remain unsupported.

<a id="49-static-string-element-comparisons-and-shared-arguments-2026-09-15"></a>

### 4.9. Static String Element Comparisons and Shared Arguments

Static string elements of owned local tuples/arrays support all six comparisons and ref/string arguments without owned temporaries. BorrowStringElement protects the located path through later operands/arguments and cleanup; disjoint sibling operations remain possible. Constructed partially moved parents allow only initialized remaining elements. Dynamic Non-Copy shared-result paths and general retained-borrow dependencies remain incomplete.

<a id="410-static-string-element-borrowing-from-owned-parameters-and-temporaries-2026-09-15"></a>

### 4.10. Static String Element Borrowing from Owned Parameters and Temporaries

Static string borrowing also accepts owned parameters, construction/direct-call/selection results after their verified production/completion/Join. Owners live to the prescribed endpoint; ending the Loan does not destroy them early. Conditional production/cleanup and defer/loop lifetimes use validated flags. Temporary mutation/partial Move and unrestricted borrowed-receiver element paths remain unsupported.

<a id="411-static-partial-moves-from-owned-parameters-2026-09-15"></a>

### 4.11. Static Partial Moves from Owned Parameters

Owned tuple/array parameters support static Non-Copy element Move and use/borrow of remaining parts with actual argument storage. Parameters remain let-like: updates/reinitialization reject. Receipt initializes sparse-path flags; remaining parts destroy in reverse logical order after result acquisition. Dynamic Non-Copy acquisition and partial Moves from temporary/function/selection results remain unsupported.

<a id="c2-builds-modules-and-source-artifacts"></a>

<a id="c11-executable-preparation-and-sequence-review-2026-09-09"></a>

<a id="c13-remaining-implementation-selections"></a>

<a id="c22-lowering-and-windows-profile-specification-integration-2026-09-11"></a>

<a id="c25-core-declaration-catalog-and-native-backend-candidate-2026-09-11"></a>

<a id="c35-shared-llvm-version-policy-2026-09-13"></a>

<a id="c36-native-build-execution-and-shared-package-release-2026-09-13"></a>

<a id="c37-generated-kernel32-import-library-2026-09-13"></a>

## 5. CLI, Artifacts, and Windows Runtime

- [Project](Kimi/SolutionAndProject/Project.cs) Check performs semantic validation; Generate/`emit` produces consistent `.ll` and `.link.json` files; Build/`build` produces a native exe. `run` executes an existing Application without automatically rebuilding after source changes.
- [Solution](Kimi/SolutionAndProject/Solution.cs) shares input resolution across build/run/emit: try the specified path, then `.kimiproj` if no extension was given, then `.kimi`. Failure of a selected target does not fall back to another candidate. A single `.kimi` becomes an implicit Application/O2 project containing only that file, with its target selected from the OS/OS architecture (currently Windows x64; `--Target` can override it). It includes no neighboring sources/project settings and creates no `.kimiproj`. run validates and executes existing artifacts without reading source contents. The CLI command is consistently `emit`; unknown commands, including legacy `emit-llvm`, exit with 1.
- [EmissionArtifacts](Kimi/Compiler/Emission/EmissionArtifacts.cs) / [NativeToolchain](Kimi/Compiler/Emission/NativeToolchain.cs) handle schema 3, IR/exe/supply hashes, ABI and LLVM identity, invalidation of previous success on failure, and publication from staging. They collect tool output and support cancellation, time limits, and child-process-tree termination. Application output/exit status is forwarded.
- [ToolchainResolver](Kimi/Compiler/Emission/ToolchainResolver.cs) selects the root from CLI ToolchainRoot, environment variable KIMI_TOOLCHAIN_ROOT, then the default location. The default is beside the executable; source builds search the checkout's toolchain directory. LlvmBin and explicit backend-path overrides are handled separately. emit alone needs no LLVM installation. Ordinary .NET builds do not regenerate the backend.
- [WindowsProfile](Kimi/Compiler/Emission/WindowsProfile.cs) and [profile.json](backend/windows-x64/profile.json) define LLVM 22.1.8, backend ABI 2, and __chkstk/memcmp/memcpy/memmove/memset. Versions are shared with Directory.Build.props; actual archive hashes are checked too. Integrity checks remain mandatory even for explicit unpinned-toolchain trials.
- [WindowsRuntime.ll.in](Kimi/Compiler/Emission/WindowsRuntime.ll.in) implements startup, UTF-8 output, allocation/freeing, string destruction, and Abort. Normal exit is 0; Abort is 1. LF is written separately and NUL is treated as data. Static backing is not freed; Heap responsibilities are released. General object/Weak/metadata runtime support remains incomplete.
- [Kernel32Imports](Kimi/Compiler/Emission/Kernel32Imports.cs) generates and validates an import library from project-owned definitions using llvm-dlltool. It supplies seven ordinary-runtime APIs, two test-runtime APIs and three native-harness APIs, removing the need to configure the SDK's kernel32.lib.

Evidence: EmissionArtifacts / NativeToolchain / ToolchainResolver / Kernel32Imports / MinimalEmission tests and [backend verification scripts](backend/windows-x64). See §7 for the verification scope and links to saved records.


Project-only dependency graphs load real source modules with exact identities, settings/source bytes, cycle/conflict checks and direct reference visibility; each dependency body is checked in its defining environment. `restore` maintains deterministic product/test lock partitions and atomic replacement; product source commands validate but never rewrite locks. Source changes trigger fresh semantic checks. Legacy nonempty `KotonohaArray` and malformed/duplicate identities reject.

Supported source-module common generation is verified for transitive calls, shared generic identity, owned strings, defaults, library `main`, immutable scalar static reads and root tests calling dependencies, with O0/O2 native evidence. Dependency tests remain excluded and failed dependency builds invalidate executable success records. Library inspection is supported for the same verified body subset; it is not a source package or public native ABI. Package loading/pack/publish/store commands, portable content/semantic records and full module/native input records remain incomplete. Source serialization reparses/rebinds; it is not persisted authoritative semantic proof. Project configuration validates `NativeRequirements` (Kind, optional ContractId/Sha256) and self-targeted `NativeLibraries` map records at load and emission, including requirement expansion/agreement, repeated target/name rejection, the `kimi_backend` Sha256 assertion and the `NativeBindings` migration diagnostic; the `Name`/`Input` array form, `Package`-targeted supplies and actual native supply/kind checks remain incomplete. The existing schema-3 manifest does not certify full module-scoped native requirement/supply validation.

## 6. Unconnected Product Features and Performance Infrastructure

| Area | Implemented scope and remaining limits |
| --- | --- |
| Language tests | Product mode excludes TestSources without reading them, skips test dependency resolution and removes inline #Test bodies from ordinary lookup/startup/analysis/generation while retaining selected syntax/target diagnostics. The adopted Windows x64 profile now implements discovery/all-body checking, `$expect`/`$require`, project/solution execution and bounded isolated workers. See the verified boundary below. |
| Mods and composition | Append/rebind/provisional-final source infrastructure exists; `Compilation.Bind` does not execute Mods. Public host/query/marker interfaces remain G1. Composition Root Entry/Provider selection is withdrawn/unsettled, not an implemented feature. |
| LSP and KimiCode | LspServer has initialization/document/shutdown communication; open/change diagnostic publication and dump integration remain unconnected. KimiCode uses an adjacent Debug DLL and DebugWait=true; general server configuration/host tests are not established. |
| CI and distribution | `.github/workflows/test.yml` explicitly selects Linux Release, xUnitTest project, nonzero-test minimum and serial tests. `publish.yml` retains a less explicit test command and manual NuGet pack/push. These workflows are distinct; neither execution nor publication was performed by this migration. Windows native/PR coverage and evidence retention still need review. |
| Performance | Binding/type/CFG/ABI/layout/constant buffers reuse capacity; targeted warm tests measure zero allocation on their own workloads. Shared module preparation, graph loading, process startup and every Binding path are not allocation-free guarantees. Historical require-expression Binding measured 72 bytes/pass; front-end samples did not establish a speedup. The original extended benchmark input failure remains a separate finding. |

Programs 1–16 have Release target-level native coverage, including Debug/Release O0/O2 target and variant coverage for programs 15–16. Programs 17–21 retain failed O2 build probes, not executable support. Programs 22–38 are planned. Parser coverage and adopted documentation do not establish end-to-end executability. User constructor, closure, generic, reference and sequence support should be read from the bounded areas above, not older blanket omissions.

## 7. Verification Records

| Verification scope | Saved result / code state | Evidence and limits |
| --- | --- | --- |
| Kimi library organization (KL) | Warning-free Debug/Release builds; 8,998 managed tests each; eight frozen fixtures / 16 native O0/O2 runs; warm Bind allocation remains zero | [History and bounded performance comparison](PLAN_HISTORY.md#kimi-library-organization). Final generated fixture bytes match the executed inputs. Source/API organization does not add rc/arc/Weak runtime support. NativeAOT NOT_RUN. |
| 38-program restructuring | PASS: warning-free Release build, 57 alias/syntax tests, 577 program 1–12/14 harness checks and two program-13 O0/O2 runs; programs 15–21 O2 builds FAIL | [Record](PLAN_HISTORY.md#programs38-restructure), [input/report manifest](bin/milestones38-20260918-212937/verification.json). Sources 1–21 exist; 22–38 remain planned. No compiler feature changes; Debug/full managed/NativeAOT NOT_RUN. |
| Historical programs 18–20 authoring (before split) | Release compiler/test-project build PASS, zero warnings/errors; one existing syntax-catalog test PASS; three native O2 builds FAIL, native tests NOT_RUN | [Record and commands](PLAN_HISTORY.md#programs18-20-design), [input hashes/logs](bin/milestones18-20-design-20260918/verification.json). No compiler changes in this task. Debug, O0, full managed regressions and NativeAOT NOT_RUN. |
| Program 14 and current regressions | PASS on the input manifest and Debug/Release DLLs in the frozen audit: 8,670 managed tests each; 96 target checks; 52 program 1–13 native runs; 68 related fixture sets / 136 O0/O2 executions | [Audit](bin/milestone14-work-20260918/verification.json), [failure/fix history and exact commands](PLAN_HISTORY.md#program14-completion). LLVM 22.1.8 input/optimized-IR verification, exact UTF-8/LF output, exit values and destruction order are checked. The Debug/Release fixture bytes match; they are executed once per optimization, not counted twice. NativeAOT NOT_RUN. |
| Program 13: Slice-backed Iterator | Historical target PASS: warning-free Debug/Release compiler builds; four native runs, O0/O2 per configuration; exact three-line stdout, exit 0, empty stderr | [Debug](bin/milestone13-work-20260918/Debug/verification.json), [Release](bin/milestone13-work-20260918/Release/verification.json), [source state, commands and limitations](PLAN_HISTORY.md#program13-completion). Normal builds verify input IR and optimized O2 IR with pinned LLVM 22.1.8. Dedicated negative tests, test suites, existing Milestones and NativeAOT are NOT_RUN for this final state. |
| Units 62–67 regression audit and current regressions | PASS: warning-free Debug/Release builds, 8,673 managed tests each; 3,147 scalar fixture sets / 6,294 O0/O2 executions; programs 1–14, emission and CLI scripts per configuration; all buildable examples | [Record](PLAN_HISTORY.md#units62-67-verification), `bin/verification-20260918-units62-67/`. Fixes a false control-flow cascade after unsupported match guards. |
| Terminal guards and owned Patterns, units 62–67 | Historical focused PASS: warning-free compiler/test-project builds, 37 newly authored managed cases per Debug/Release configuration, 11 new fixture sets / 22 O0/O2 executions | [Execution audit](bin/plan-execution/20260918-053259/verification.json), [record](PLAN_HISTORY.md#terminal-owned-patterns-20260918). Exact cleanup counts/order, Abort/divergence, enum Copy/Move, UTF-8 literal tests and serialized reload are covered. All 55 IR/oracle files match across configurations; reuse is not counted twice. Their omitted regressions were later run in the audit above. |
| Existing continuation and prepared-storage verification | Historical PASS: warning-free Debug/Release builds, 8,601 managed tests each, 2,152 standalone plus 20 dedicated O0/O2 executions, 1,058 program checks for 1–12 | [Final audit](bin/plan-verification/20260918-041105/verification.json), [failures/fixes and identity mapping](PLAN_HISTORY.md#continuation-verification-20260918). Sixteen new cases and corrected legacy expectations cover the bounded implemented slices. NativeAOT NOT_RUN; no new language feature or whole-compiler conformance claim. |
| Guarded match/Copy-tuple default implementation checkpoint | Historical PASS: warning-free Debug/Release builds, 28 newly authored managed cases each, four fresh O0/O2 native executions | [Evidence](bin/plan-execution/20260918-033541/verification.json), [execution record](PLAN_HISTORY.md#guarded-continuation-20260918). Existing regressions were deferred then; the preceding verification closed those bounded gates for its source state. |
| Bounded loop/match/default implementation checkpoint | Historical PASS: warning-free Debug/Release builds, 25 newly authored managed cases each, six fresh O0/O2 native executions | [Evidence](bin/plan-execution/20260918-030539/verification.json), [execution record](PLAN_HISTORY.md#bounded-continuation-20260918). Existing regressions were deferred then and subsequently executed against the separately recorded continuation-verification source state; this is not a program-13 regression result. |
| Program 12 implementation and regressions | PASS: warning-free Debug/Release builds, 8,531 tests each, 1,058 program checks for 1–12, 234 standalone O0/O2 executions | [Frozen audit](bin/milestone12-work-20260918/verification.json), [execution details](PLAN_HISTORY.md#program12-completion). Required native checks ran; NativeAOT was NOT_RUN as instructed. No claim of full language conformance. |
| Pre-program-12 full managed run and builds | PASS: clean Debug/Release, 8,507 tests each, 94 added in units 48–51 | [20260918-003029 verification.json](bin/plan-execution/20260918-003029/verification.json), [execution record](PLAN_HISTORY.md#execution-1). Source/DLL hashes matched the earlier migration checkout; these are historical results, not the changed program-12 compiler. |
| Pre-program-12 native families | PASS: Default 182, WholeValue 15, Never 664 fixtures, 861 total / 1,722 O0/O2 executions, 122 new fixtures | [native summary](bin/plan-execution/20260918-003029/final-native-regressions.json), [fixture identities](bin/plan-execution/20260918-003029/final-fixture-hashes.json). This is not every native fixture in the repository. |
| Pre-program-12 programs 1–11 | PASS: 984 checks across Debug/Release; all 22 reports inspected for status/count/source/compiler match | [program audit](bin/plan-execution/20260918-003029/final-program-audit.json). The overlapped initial program results were superseded by isolated sequential runs, even where an initial report said PASS. |
| Previous units 43–47 | Recorded PASS: 8,413 managed/configuration, 255 unique fixtures / 510 native executions and 984 program checks | [history](PLAN_HISTORY.md#execution-2); its evidence directory exists. Earlier intermediate `ah-*` results do not certify the `ah2-*` region correction. Not relabeled as a new migration run. |
| Alias, nesting, replacement and earlier feature increments | Historical counts, failures, fixes and stage boundaries retained verbatim | [STATUS records](PLAN_HISTORY.md#status-record-index) and [area verification snapshot](PLAN_HISTORY.md#status-area-snapshot). `bin/kimi-alias-verification.json` is absent; nesting/replacement adoption summaries do not identify retrievable per-run report paths. Later suite/native coverage applies only to its recorded families. |
| Early plan/program evidence | Many ignored run directories and original target reports are absent | [evidence availability](PLAN_HISTORY.md#migration-audit). Original commands/hashes/diagnostics remain recorded; a textual reference is not treated as an existing artifact. Latest program regressions do not reconstruct every older feature-specific check or physical sharing inspection. |
| Performance and NativeAOT | Historical 4/16/64-guard history counts, zero measured warm ownership allocation, and zero warm reload/IR allocation; NativeAOT NOT_RUN | Timings under verification load are observations, not controlled throughput benchmarks. No universal allocation/speed/retention claim; NativeAOT was neither run nor made a requirement. |

The earlier 2026-09-18 documentation migration performed identity/link checks only; the subsequent program-12 implementation ran the separately identified final builds, tests and native executions above. All saved PASS results retain their original scope and input state. Reverify affected behavior after source/tool/artifact changes according to PLAN; do not repeat builds/full suites solely for documentation reorganization.

### 7.1. Non-Copy Element Replacement and Element Operations (2026-09-15)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#71-non-copy-element-replacement-and-element-operations-2026-09-15). These counts are not a current run.

### 7.2. Verification of the Preceding Increments (2026-09-14)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#72-verification-of-the-preceding-increments-2026-09-14). These counts are not a current run.

### 7.3. CLI Input Resolution and Implicit Projects (2026-09-15)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#73-cli-input-resolution-and-implicit-projects-2026-09-15). These counts are not a current run.

### 7.4. Static Partial Moves and Destruction of Remaining Parts (2026-09-15)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#74-static-partial-moves-and-destruction-of-remaining-parts-2026-09-15). These counts are not a current run.

### 7.5. Static String Element Borrowing (2026-09-15)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#75-static-string-element-borrowing-2026-09-15). These counts are not a current run.

### 7.6. Element Borrowing from Owned Parameters and Temporaries (2026-09-15)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#76-element-borrowing-from-owned-parameters-and-temporaries-2026-09-15). These counts are not a current run.

### 7.7. Partial Moves from Owned Parameters (2026-09-15)

Historical verification moved to [PLAN_HISTORY.md](PLAN_HISTORY.md#77-partial-moves-from-owned-parameters-2026-09-15). These counts are not a current run.

## 8. autoframe Execution Infrastructure

The previously referenced `autoframe/SPEC.md`, `autoframe/README.md` and `autoframe/VERIFICATION.md` are **absent from this checkout**. Current infrastructure support cannot be confirmed here. The 56 simulated-test result and statement that the actual CLI was not retested remain in [the historical snapshot](PLAN_HISTORY.md#8-autoframe-execution-infrastructure). This migration did not execute or reverify autoframe.

<a id="numeric-literal-replacement"></a>
[Numeric literal replacement](#2-binding-types-and-kimi) — current coverage above; [original record](PLAN_HISTORY.md#numeric-literal-replacement).

<a id="unknown-attribute-validation"></a>
[Unknown Attribute validation](#2-binding-types-and-kimi) — current coverage above; [original record](PLAN_HISTORY.md#unknown-attribute-validation).

<a id="generic-signature-certificate-validation"></a>
[Generic signature certificate validation](#2-binding-types-and-kimi) — current coverage above; [original record](PLAN_HISTORY.md#generic-signature-certificate-validation).

<a id="inherited-declaration-names"></a>
[Inherited declaration Names](#2-binding-types-and-kimi) — current coverage above; [original record](PLAN_HISTORY.md#inherited-declaration-names).

### Language test execution

`kimi test` accepts project/source/directory/solution inputs. It validates every selected body before filtering or listing, including ordinary startup bodies, without executing startup or requiring a product entry. TestSources and root TestDependencies participate only in test analysis; dependency tests are not discovered. `--list` creates no native artifacts. Solution execution builds all required project artifacts before launching cases and shares one jobs limit and total retention budgets.

`$expect` and `$require` use the existing ownership CFG: evaluate once, latch false before condition cleanup, evaluate the optional message lazily, clean up its temporary, then continue or send a distinct require-Abort. Concrete bodies and supported shared generic helpers lower these operations. Reports retain original scalar comparison bits, explicit nested-message identities and cleanup phases. Passing checks allocate no diagnostic storage in the child. `Kimi.Test.tempDirectory` returns an independently owned path captured before user code; TMP/TEMP name the same case directory.

Each case starts in a Windows Job Object with no breakaway, with EOF stdin and separately drained result/stdout/stderr channels. Deadlines, recovery grace, frame/detail/log budgets and whole-run limits are finite. JSON/text outcomes distinguish assertion failure, Abort, timeout, cancellation and management errors. Artifacts have canonical identities and executable hashes and remain pinned during execution. Result publication uses atomic rename; retention removes only recognized completed runs.

Debug/Release builds are warning-free and each full managed suite passes 9,144 tests. Native O0/O2 profile verification passes with deliberate failure cases checked against expected outcomes. Evidence is recorded in [the test-profile checkpoint](PLAN_HISTORY.md#test-profile-implementation-20260919). `TestExecutionAnalysisTest`, `TestProtocolTest` and `TestProcessRecoveryTest` cover semantic isolation, malformed/stale records, zero retention, cancellation, missing completion and descendant recovery. `backend/windows-x64/test-testing.ps1` verifies O0/O2 execution, generic/integer/float snapshots, lazy/nested messages, cleanup, require-Abort, timeout, logs, temporary removal and solution barriers.

Existing compiler limits still apply to test bodies: dynamic static initialization/shutdown, full module/native input records and full generic resource-plan separation are unfinished (PLAN I11/I16/I28/I30/I31). Supported source-module calls execute from root tests; dependency tests are excluded. Unsupported generation fails before case launch; listing may succeed for semantically valid code that the native backend cannot yet emit. Named execution profiles remain deferred by the specification. NativeAOT was not run.
