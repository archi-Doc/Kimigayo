# Appendix E. Terminology index

[Specification index](../../SPEC.md)

This index is a reading aid. The linked sections contain the authoritative definitions and restrictions.

| Term | Meaning | Defined in |
| --- | --- | --- |
| Access Designator | A resolved access target, without a promise of storage or Consume permission. | [Value model](../03-types-and-values.md#34-values-places-and-storage) |
| Adaptation Target | Core or object View Target and Semantics requested by `@`; result Origins are inferred. | [Explicit operations](../13-operators-and-assignment.md#1351-forms-and-adaptation-targets) |
| Alias | A source-local resolved Container reference that opens direct members or introduces a named Qualifier. | [Source aliases](../18-modules-and-dependencies.md#181-external-references-and-aliases) |
| API signature | Exposed Types and requirements checked for accessibility, beyond overload identity. | [API signature accessibility](../09-names-signatures-and-access.md#932-api-signature-accessibility) |
| Associated Type | Ordinarily a Core binding fixed by explicit Type-identity facts; Kimi iteration Element requirements have the explicit complete-Type exception in §22.1. | [Associated Types](../08-generics-constraints-and-contracts.md#843-associated-types) |
| Binding | Associating source names and operations with declarations and meanings. | [Name resolution](../09-names-signatures-and-access.md#9-names-signatures-and-access) |
| Binding Identity / Value Instance | Resolved binding / its currently held value | [Stable bindings](../14-control-flow.md#14101-stable-bindings-and-effective-types) |
| Body | A scoped single expression/statement after `=>`, or an indented sequence whose direct expression values are discarded. | [Body forms and results](../14-control-flow.md#142-blocks-and-evaluation-contexts) |
| Callable / Call Receiver Requirement | Declared generic access / concrete minimum body access | [Callable constraints](../08-generics-constraints-and-contracts.md#86-callable-constraints), [call receivers](../07-functions-and-callable-values.md#763-call-receiver-and-acquisition) |
| Candidate Place | Initialized matched storage designated for guard reading and later body acquisition. | [Guards](../14-control-flow.md#1483-guards) |
| Case / Payload | An enum alternative / its attached positional data. | [Enums](../06-declarations-and-containers.md#63-enums) |
| Closure / Environment / Capture | A callable body and values acquired when it is created | [Function expressions](../07-functions-and-callable-values.md#76-function-expressions) |
| CodeContext | Source-local lookup and diagnostic context for one immutable source snapshot. | [Compiler requirements](A-compiler-requirements.md#appendix-a-compiler-implementation-requirements) |
| Compilation | One Project processed under fixed source, dependency, target, and build inputs. | [Build units](../20-compilation-configuration.md#201-build-units) |
| Compilation root | Lookup entry point for the project root and direct-dependency reference names. | [Modules](../18-modules-and-dependencies.md#18-modules-and-dependencies) |
| Compiler-intrinsic Contract | A compiler-recognized Contract with individually specified language effects or derivation rules. | [Intrinsic Contracts](../08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees) |
| Complete value | An aggregate with completed construction and all stored fields Initialized. | [Construction and completeness](../15-ownership-and-lifetime-analysis.md#1512-aggregate-construction-and-completeness) |
| Completion | Normal or abrupt completion of evaluation, distinct from divergence and Abort Termination. | [Completions](../14-control-flow.md#141-completions) |
| Composition Root | Reserved root for built-in abort and test verification operations; Entry/Provider extensions remain unsettled. | [Reserved syntax](../13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax) |
| Test definition / Case | A Test function / its independently executed and reported unit; initially one case per definition. | [Test declarations](../06-declarations-and-containers.md#651-test-definitions) |
| ArtifactId / TestId / CaseId / SiteId / IssueId | Test artifact, declaration, case, verification site and failure-occurrence identities. | [Test diagnostic identity](../22-core-execution-and-foreign-functions.md#2263-diagnostic-identity) |
| Conditional Conformance | A verified conformance path available when its declared Type-argument conditions are Proven. | [Conditional conformance](../08-generics-constraints-and-contracts.md#848-conditional-conformance) |
| Conformance | A Type's fulfillment of a Contract, including associated-Type bindings and requirement-to-implementation mappings. | [Conformance](../08-generics-constraints-and-contracts.md#844-conformance) |
| Conformance Identity | The pair of concrete Type Identity and Contract Identity, shared by explicit and refinement-implied conformance. | [Conformance](../08-generics-constraints-and-contracts.md#844-conformance) |
| Constraint | A condition imposed on a Type or Type Semantics. | [Constraints](../08-generics-constraints-and-contracts.md#82-constraints) |
| Constraint Clause | A declaration clause expressing a Constraint as `subject is requirement`. | [Constraints](../08-generics-constraints-and-contracts.md#82-constraints) |
| Constraints | The set of conditions required for a declaration to be valid or usable. | [Constraints](../08-generics-constraints-and-contracts.md#82-constraints) |
| Consume | Non-Copy value acquisition that transfers ownership/capability from a Movable Place. | [Movable Places](../15-ownership-and-lifetime-analysis.md#1515-movable-places) |
| Consume Eligibility | Whether the declaration, Type, and path provide the Consume operation. | [Consume verification](../15-ownership-and-lifetime-analysis.md#1514-consume-verification-and-representation) |
| Consume Legality | Whether the current use site may perform an eligible Consume. | [Consume verification](../15-ownership-and-lifetime-analysis.md#1514-consume-verification-and-representation) |
| Contract | A capability declaration containing requirements, associated Types, and Constraints, without implementations or storage. | [Contracts](../08-generics-constraints-and-contracts.md#84-static-contracts) |
| Contract refinement | Inheritance of all parent requirements and Constraints; conformance entails ancestor conformance. | [Refinement](../08-generics-constraints-and-contracts.md#842-refinement) |
| Copy | Implicit value duplication that leaves its source initialized. | [Copy and Move](../03-types-and-values.md#35-copy-and-move) |
| Core | A Type's value kind, structure, and identity, distinct from its outer Semantics and Origins. | [Type composition](../03-types-and-values.md#3-types-and-values) |
| Declaration Container | A named declaration scope with members permitted by its kind. | [Containers](../06-declarations-and-containers.md#61-declaration-containers) |
| Deferred Block | Cleanup code registered by `defer` for its containing scope's exit. | [Deferred Blocks](../16-scope-exit-and-destruction.md#161-deferred-blocks) |
| Deferred Obligation | A legitimate dependent check retained with its evidence, environment, and deadline. | [Generic checking](../08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations) |
| Delimiter region / Body scope | Syntactic nesting region / local name and cleanup scope. | [Layout](../02-source-and-lexical-structure.md#22-lines-indentation-and-continuation), [Body scopes](../14-control-flow.md#1431-body-forms-and-scopes) |
| Destruction responsibility | Responsibility for ending an owned value's lifetime under the cleanup rules. | [Value model](../03-types-and-values.md#34-values-places-and-storage) |
| Directive Binding | Resolution and validation of compile-time Condition names and dependencies. | [Compiler requirements](A-compiler-requirements.md#appendix-a-compiler-implementation-requirements) |
| Discard Context | An evaluation context that does not retain an expression's result. | [Evaluation contexts](../14-control-flow.md#142-blocks-and-evaluation-contexts) |
| Do expression | Executes a scoped body once; an optional label receives named exit. | [Do expressions](../14-control-flow.md#1432-do-expressions) |
| Dynamic Type / Runtime Type Identity | Actual constructed Core / its runtime comparison identity | [Object views](../03-types-and-values.md#335-object-views-and-identity), [metadata](../21-layout-runtime-and-code-generation.md#2121-type-identity-and-descriptors) |
| Effective access domain | Source contexts permitted by a declaration's access and enclosing restrictions. | [Access domains](../09-names-signatures-and-access.md#931-effective-access-domains-and-protected-receivers) |
| Effective Type / Flow State | Point-specific guaranteed Type / coordinated analysis facts | [Refinement](../14-control-flow.md#1410-type-refinement) |
| Environment Condition | A Boolean directive expression over fixed target and Project settings. | [Condition evaluation](../19-compile-time-directives.md#193-condition-evaluation-and-selection) |
| Finalization | Acceptance of a declaration, layout, specialization, or body after required checks are resolved. | [Compiler terminology](A-compiler-requirements.md#appendix-a-compiler-implementation-requirements) |
| Function Item / common Function Type | Concrete declaration identity / shared erased calling contract | [Callable Types](../03-types-and-values.md#321-callable-value-types) |
| GenericArity / OriginArity | Generic slot count (one pair consumes one slot) / a Type's own scalar Origin schema size. | [Signatures](../09-names-signatures-and-access.md#91-signatures) |
| Getter result Type | The declared result of custom/computed/required get; matches its Property header Type. | [Accessor contracts](../11-properties.md#112-accessor-functions) |
| Instantiation / explicit full specialization / automatic specialization | Argument binding / mandatory user implementation selection / meaning-preserving Type-specific code generation. | [Generic code generation](../21-layout-runtime-and-code-generation.md#213-generic-code-generation) |
| Koto | A compiler syntax-tree node. | [Compiler terminology](A-compiler-requirements.md#appendix-a-compiler-implementation-requirements) |
| Kotonoha | One named module; initial external distribution uses source packages. | [Modules](../18-modules-and-dependencies.md#18-modules-and-dependencies) |
| PackageId / PackageVersion / ReferenceName | Defining module/release identities / a consumer's direct source name. | [Dependency identities](../18-modules-and-dependencies.md#1841-identities-and-graph) |
| SourceId / ProjectSnapshotId | Source-package content identity / live Project product snapshot identity. | [Package identity](../18-modules-and-dependencies.md#1863-content-identity-and-integrity), [input records](../18-modules-and-dependencies.md#1852-immutable-processing-records) |
| ModuleInputId / DeclarationKey | Whole-module verification fast-path key / revision correspondence key, neither a replacement for actual Type identity. | [Verified information](../18-modules-and-dependencies.md#187-verified-information-and-reuse) |
| Dependency lock / publication store | Project resolution state updated by restore / local release mappings updated by publish. | [Locks](../18-modules-and-dependencies.md#1851-resolution-state), [stores](../18-modules-and-dependencies.md#1864-user-cache-and-publication-stores) |
| Mod / ModId | One registered source-generation step / its stable identity within a Compilation. | [Mods](../20-compilation-configuration.md#207-mods-source-generation) |
| Kimi Kotonoha | The compiler-compatible foundation module referenced as `Kimi`. | [Required declarations](../22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) |
| Loan | A borrowed place, access mode, and validity region. | [Borrow checking](../15-ownership-and-lifetime-analysis.md#156-borrow-checking) |
| Lookup environment | Declarations and aliases available for lookup in a scope; extensions are a future design. | [Name resolution](../09-names-signatures-and-access.md#9-names-signatures-and-access) |
| Move | Transfer of a value and responsibility or capability, marking its source Moved; requested by `@move` and its owning-Semantics spellings. | [Copy and Move](../03-types-and-values.md#35-copy-and-move) |
| Bare acquisition | Acquisition of an expression without an explicit `@` operation: Copy, shared borrow or Reborrow, never a Move of a Place. | [Copy and Move](../03-types-and-values.md#35-copy-and-move) |
| Lending rule | A spelling is required exactly where a directly owned Place is first lent exclusively or given away. | [Movable Places](../15-ownership-and-lifetime-analysis.md#1515-movable-places) |
| Access path | How a Place is reached: directly, or through an exclusive or shared reference; it bounds the borrows of the Place. | [Value model](../03-types-and-values.md#34-values-places-and-storage) |
| Borrow value | An expression whose outer Semantics is in the `borrow` category. | [Value model](../03-types-and-values.md#34-values-places-and-storage) |
| Move Path | A statically tracked path with independent initialization state and destruction responsibility. | [Move Paths](../15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move) |
| ObjectCallCompatible | Public receiver-preservation guarantee for calls through object borrows or base-subobject projections, per call operation | [Object calls](../12-expressions.md#1244-object-receiver-compatibility) |
| ObjectViewCompatible | Contract eligibility as an Object View Target with fixed associated Types, within the runtime Contract extension | [Runtime contracts](../08-generics-constraints-and-contracts.md#85-runtime-contracts) |
| Sealed | Intrinsic evidence that the normalized outer Type is a valid owner Core other than Never or an open struct; independent of stored fields, Copy and Owned | [Sealed](../08-generics-constraints-and-contracts.md#8471-sealed) |
| Whole-value update | Replacement or exchange of the complete contents of initialized authorized storage, preserving required dependencies | [Whole-value updates](../15-ownership-and-lifetime-analysis.md#157-whole-value-updates) |
| Complete payload projection | A same-target Sealed object payload borrowed as ordinary ref/uniq with retained owner and referent dependencies | [Payload projection](../13-operators-and-assignment.md#13551-complete-object-payload-projection) |
| Owned / OwnedOrigins | Lifetime independence from non-static dependencies / the conservative Origin closure proving it | [static and Owned](../15-ownership-and-lifetime-analysis.md#1523-static-and-owned) |
| Origin | A set of program points where a borrow is guaranteed valid. | [Origin expressions](../15-ownership-and-lifetime-analysis.md#1521-origin-expressions) |
| Origin binding set | A Type occurrence's mapping from schema slots to Origins; a suffix may name it. | [Binding sets](../15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets) |
| Origin projection | Selection of a declared slot from a binding set or value Type, without runtime evaluation. | [Projection](../15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets) |
| Origin relation | Declaration-attached equality or outlives requirement; it creates no Loan authority. | [Relations](../15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations) |
| Partial Move | Transfer of an aggregate's part, leaving the aggregate incomplete. | [Move Paths](../15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move) |
| Pattern / Guard | Structural or binding syntax / an optional Boolean test selecting a match arm | [Match](../14-control-flow.md#148-match-expressions-and-patterns) |
| Place | A storage location that can hold a value. | [Value model](../03-types-and-values.md#34-values-places-and-storage) |
| Project root | Root of the primary Kotonoha's declaration hierarchy. | [Modules](../18-modules-and-dependencies.md#18-modules-and-dependencies) |
| Provisional Binding | Mod-time semantic results that do not constrain final Binding. | [Mod Binding](../20-compilation-configuration.md#2072-compilation-and-binding) |
| Property Witness Mapping | Verified requirement-to-Property operations with Type/Origin substitutions and optional standard-operation bridges. | [Witness adaptation](../11-properties.md#1142-standard-operation-witnesses) |
| Field | The storage slot of a let/var Property; source access obeys its accessor permissions. | [Stored Properties](../11-properties.md#11-properties) |
| Property | A let/var stored member or computed operation member; a Contract property requires operations. | [Properties](../11-properties.md#11-properties) |
| Reborrow | A borrow derived from an existing borrow, subject to the parent's capability and Origin. | [Reborrowing](../15-ownership-and-lifetime-analysis.md#1563-reborrowing) |
| Result source / Target Result Type / Expression Type | A value-supplying site / its target constraint / the checked expression's Type. | [Results](../14-control-flow.md#149-result-validation) |
| Scalar | Integer, floating-point, Boolean, or Character Core; short for Primitive scalar. | [Primitive cores](../03-types-and-values.md#31-primitive-cores) |
| Semantics | A value's representation, ownership, borrowing, access, and safety rules; also called Type Semantics. | [Type Semantics](../03-types-and-values.md#33-type-semantics) |
| SemanticsTarget | Fixed kind of a pair's direct target: a complete value Type or permitted Object View Target. | [Generic parameters](../08-generics-constraints-and-contracts.md#81-generic-type-parameters) |
| Signature | Information distinguishing declarations in the same scope. | [Signatures](../09-names-signatures-and-access.md#91-signatures) |
| SourceDocument | One immutable source input, including path and text, belonging to a Kotonoha. | [Source text](../02-source-and-lexical-structure.md#21-source-text-and-encoding) |
| Structural Completion / Runtime Reachability | Common structural path model / that model with execution state and cleanup. | [Reachability](../14-control-flow.md#1492-reachability) |
| Subject Place | Internal storage acquired once before match arm selection | [Match lifetime](../15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime) |
| Temporary Place | Anonymous storage materializing a Temporary Value. | [Materialization](../03-types-and-values.md#361-materialization) |
| Temporary Value | An expression's temporary result, distinct from its original persistent Place. | [Materialization](../03-types-and-values.md#361-materialization) |
| Transfer target / Lookup barrier | A construct receiving a transfer / a boundary stopping target lookup. | [Target lookup](../14-control-flow.md#1452-target-lookup) |
| Type | A complete type, including Semantics, its target, and all Origin dependencies. | [Type composition](../03-types-and-values.md#3-types-and-values) |
| Type-checking continuation | Unreachable-code checking that adds no execution edge. | [Unreachable checking](../14-control-flow.md#14103-type-checking-unreachable-code) |
| Value Context | An evaluation context that requires an expression's value. | [Evaluation contexts](../14-control-flow.md#142-blocks-and-evaluation-contexts) |
| View Target / Supports | Public object target / concrete-Type relationship to that target | [Object views](../03-types-and-values.md#335-object-views-and-identity) |
