# Appendix E. Terminology

[Specification index](../../SPEC.md)

A reading aid, sorted alphabetically. The linked section owns each definition.

| Term | Meaning | Owner |
| --- | --- | --- |
| Access path | How a non-raw Place is reached: Direct, through an exclusive reference or through a shared reference; Loan checking compares its steps ([§15.6.2](../15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)). Raw Places have none. | [§3.4.1](../03-types-and-values.md#341-access-paths-and-value-kinds) |
| Acquisition mode | What a parameter requires of a bare owned Place argument: ByValue, Shared or Exclusive, by the outer Semantics of its Type. | [§7.4.1](../07-functions-and-callable-values.md#741-acquisition-modes-and-admitted-cases) |
| Adaptation Target | What `@` requests: a Semantics and Core, an inner Type or an object View Target. | [§13.5.1](../13-operators-and-assignment.md#1351-forms-and-adaptation-targets) |
| Admitted set | The Semantics a pair binder may take under its premises. | [§8.3.4](../08-generics-constraints-and-contracts.md#834-semantics-admitted-set) |
| Associated Type | A Type that each conformance fixes; it may declare Origin parameters. | [§8.5](../08-generics-constraints-and-contracts.md#85-associated-types) |
| Bare acquisition | Acquisition of an expression written without an `@` operation. | [§3.5](../03-types-and-values.md#35-copy-and-move) |
| Binding set / Origin projection | A Type occurrence's mapping from Origin slots to Origins / selecting one slot. | [§15.3.1](../15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets) |
| Borrow value | A value whose outer Semantics is `ref`, `uniq`, `objref` or `objuniq`. | [§3.4.1](../03-types-and-values.md#341-access-paths-and-value-kinds) |
| Built-in numeric Type | A numeric Type, a Type parameter with proven `PrimitiveInteger`, or `Wrapping<T>` of such a parameter. | [§13.3.3](../13-operators-and-assignment.md#1333-arithmetic-contracts) |
| Call Receiver Requirement | The minimum access a callable body needs of its receiver. | [§7.7.3](../07-functions-and-callable-values.md#773-call-receiver) |
| Callable Constraint | A Constraint stating how a generic value can be called. | [§8.10](../08-generics-constraints-and-contracts.md#810-callable-constraints) |
| Case / Payload | An enum alternative / its positional data. | [§6.5](../06-declarations-and-containers.md#65-enums) |
| Closed Contract | `Position` or `PositionRange`: the Kimi Kotonoha fixes the conforming Types. | [§8.7.5](../08-generics-constraints-and-contracts.md#875-closed-contracts) |
| Closure / Capture | A callable body with its environment; each capture entry initializes one environment binding, as `let x = x` or `var x = x` would. | [§7.7.2](../07-functions-and-callable-values.md#772-captures) |
| Common adaptation | The single operation selected at a position with a fixed expected Type. | [§10.2](../10-overload-resolution-and-inference.md#102-common-adaptation) |
| Compilation root | Per Kotonoha: its root declarations, the reference names of its direct dependencies, and `Kimi`. | [§18](../18-modules-and-dependencies.md#18-modules-and-dependencies) |
| Compiler-intrinsic Contract | Copy, Owned, Callable, Sealed, ObjectPayload or PrimitiveInteger. | [§8.7.1](../08-generics-constraints-and-contracts.md#871-compiler-intrinsic-contracts) |
| Complete value | A value with construction completion and current completeness. | [§15.1.2](../15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness) |
| Composition Root | `$`, the root of `$abort`, `$tryWrite`, `$expect` and `$require`. | [§13.8](../13-operators-and-assignment.md#138-reserved-syntax-and-the-composition-root) |
| Condition | A `bool` directive expression over built-in Conditions and Project settings. | [§19.2](../19-compile-time-directives.md#192-conditions) |
| Conditional Conformance | A conformance available when its conditions are Proven. | [§8.8](../08-generics-constraints-and-contracts.md#88-conditional-conformance) |
| Conditional Member | A member whose Constraint Clauses constrain its Type's parameters; applicable only when they are Proven. | [§7.5](../07-functions-and-callable-values.md#75-function-constraints) |
| `confined` | Effect bound: no environment effect and no unclassified effect. | [§8.11.2](../08-generics-constraints-and-contracts.md#8112-confined) |
| Conflicting access | Two accesses to overlapping Places or to one object's reference counts, at least one a write, not both atomic implementation accesses. | [§22.3.7](../22-core-execution-and-foreign-functions.md#2237-data-races) |
| Conformance | A Type's fulfillment of a Contract. | [§8.6.1](../08-generics-constraints-and-contracts.md#861-conformance-declarations-and-identity) |
| Conformance Identity | Complete conforming Type plus bound Contract reference. | [§8.6.1](../08-generics-constraints-and-contracts.md#861-conformance-declarations-and-identity) |
| Constraint / Constraint Clause | A condition on a Type or Semantics / its clause `subject is requirement`. | [§8.2.1](../08-generics-constraints-and-contracts.md#821-constraint-clauses) |
| Consume | The static Eligibility and Legality check of an access operation. | [§15.1.4](../15-ownership-and-lifetime-analysis.md#1514-consume-checks) |
| Contract | A Container declaring requirements, without implementations or storage. | [§8.4.1](../08-generics-constraints-and-contracts.md#841-contract-declarations) |
| Copy | Duplication that leaves the source Initialized. | [§3.5](../03-types-and-values.md#35-copy-and-move) |
| Core | A Type's value kind, structure and identity, apart from Semantics and Origins. | [§3](../03-types-and-values.md#3-types-and-values) |
| Data race | Conflicting accesses from different threads that happens-before does not order; undefined behavior. | [§22.3.7](../22-core-execution-and-foreign-functions.md#2237-data-races) |
| Declaration Container | A named declaration scope with members permitted by its kind. | [§6.1](../06-declarations-and-containers.md#61-declaration-containers) |
| Deferred Block | Cleanup code that `defer` registers for its scope's exit. | [§16.1](../16-scope-exit-and-destruction.md#161-deferred-blocks) |
| Diagnostic | A report with a severity and one category: Language, Proof, Unsupported, Input, Resource or Internal. | [§23.1.1](../23-compiler-services.md#2311-categories) |
| Direct base call | `base.f(...)`, selecting the implementation held by the direct base. | [§6.4.4](../06-declarations-and-containers.md#644-direct-base-calls) |
| Dynamic Type | The Core an object was actually constructed with. | [§3.3.1](../03-types-and-values.md#331-object-views-and-identity) |
| Effect bound | A guarantee about what a call may access, declared on a requirement, a Callable Constraint or a virtual slot: `confined` or `preserves results`. | [§8.11](../08-generics-constraints-and-contracts.md#811-effect-bounds) |
| Effective Type | The Type with the most derived Core guaranteed at a point after a refinement. | [§14.10.1](../14-control-flow.md#14101-refinable-bindings-and-effective-types) |
| Excluded syntax | Syntax a directive does not select; it gets source checks only. | [§19.4](../19-compile-time-directives.md#194-excluded-syntax) |
| Field | The storage of a `let` or `var` Property. | [§11.1](../11-properties.md#111-property-kinds) |
| Finite Origin | An Origin whose region is inferred from uses, within an upper bound. | [§15.6.5](../15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations) |
| Fixed expected call signature | The one call signature fixed by a position's expected Type: a common Function Type, or the one signature the Callable Constraints of an unbound slot `F` give at `F`, `ref/F` or `uniq/F`. | [§10.5.2](../10-overload-resolution-and-inference.md#1052-call-signatures) |
| Follow | `@follow`: selects the Place a reference or a qualifying pair layer points to, or the payload of an object handle whose View Target is a proven Sealed Type. | [§13.5.5](../13-operators-and-assignment.md#1355-follow) |
| Function Item / common Function Type | One resolved function declaration, no environment / the erased calling contract shared by callable values. | [§3.2.1](../03-types-and-values.md#321-callable-value-types) |
| Holder | A local, parameter or temporary, which carries the Loans of the values stored in every part of it. | [§15.6](../15-ownership-and-lifetime-analysis.md#156-borrow-checking) |
| Independently typable argument | A non-literal argument whose complete Type is fixed with no expectation from the call and no literal default; a nested call qualifies only with exactly one applicable candidate without an expected result, and an anonymous function never does. | [§10.5.1](../10-overload-resolution-and-inference.md#1051-argument-kinds) |
| Indexable / UniqIndexable | Contracts whose `index` and `indexUniq` return the reference that `receiver[key]` follows to its element. | [§4.7.1](../04-arrays-indexing-and-slices.md#471-index-expression-resolution) |
| Integer / wrapping integer / numeric Type | One of twelve integer Cores / `Wrapping<T>` / integer, wrapping integer or floating-point. | [§3.1](../03-types-and-values.md#31-primitive-cores) |
| Item / `LentItem(step)` | The item Type of an Iterator / of a LendingIterator, which may borrow the receiver. | [§22.2.1](../22-core-execution-and-foreign-functions.md#2221-iterator-contracts) |
| Iterable / UniqIterable / IntoIterable | The entries `for` selects for a Shared, Exclusive or ByValue Subject. | [§22.2.1](../22-core-execution-and-foreign-functions.md#2221-iterator-contracts) |
| Iterator / LendingIterator | Items survive later `next` calls / items may borrow the iterator. | [§22.2.1](../22-core-execution-and-foreign-functions.md#2221-iterator-contracts) |
| Kimi Kotonoha | The foundation module referenced as `Kimi`; its required declarations are listed in [§22.1](../22-core-execution-and-foreign-functions.md#221-required-kimi-declarations). | [§22.1](../22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) |
| Kotonoha | One named module. | [§18](../18-modules-and-dependencies.md#18-modules-and-dependencies) |
| Lending point / Loan anchor | The storage a borrow, Reborrow or projection targets. | [§3.4.2](../03-types-and-values.md#342-lending-points) |
| Lending rule | When a transfer or a new exclusive borrow must be written explicitly. | [§15.1.5](../15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition) |
| Literal argument | An untyped literal, a literal-only expression, or a control-flow expression whose result sources are all such; no evidence for a slot. | [§10.5.1](../10-overload-resolution-and-inference.md#1051-argument-kinds) |
| Literal-only expression | An untyped numeric literal, or an integer, floating-point or position/range form built from such operands by the listed operators; whether it fits a Type is decided separately. | [§12.3.3](../12-expressions.md#1233-literal-only-expressions) |
| Loan | A borrowed Place, named by its access path, and its access mode (`ref` or `uniq`), identified by its Borrow site and Semantics case; live while a value that carries it may be used; ended by destroying in place a Move Path that exactly prefixes its access path ([§15.6.3](../15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)). | [§15.6](../15-ownership-and-lifetime-analysis.md#156-borrow-checking) |
| `Loan<T>` | A zero-sized struct expressing a borrow dependency without storage. | [§15.3.5](../15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins) |
| Movable Place | A Place whose value `@move` can transfer. | [§15.1.5](../15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition) |
| Move | Transfer of a value and its destruction responsibility; on a Place, written `@move`. | [§3.5](../03-types-and-values.md#35-copy-and-move) |
| Move Path / Partial Move | A tracked path with its own initialization state / a Move of an aggregate's part. | [§15.1.3](../15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move) |
| `noinit` | Unsafe fixed-array construction that skips initial stores. | [§4.3.4](../04-arrays-indexing-and-slices.md#434-noinit) |
| Object Target | A Type over which object forms may be formed. | [§8.7.3](../08-generics-constraints-and-contracts.md#873-objectpayload) |
| ObjectCallCompatible | The receiver-preservation guarantee of calls through object borrows. | [§12.7](../12-expressions.md#127-object-receiver-compatibility) |
| ObjectPayload | Lets a value become an object's payload; `Self is not ObjectPayload` opts out. | [§8.7.3](../08-generics-constraints-and-contracts.md#873-objectpayload) |
| ObjectViewCompatible | A Contract usable as an object View Target. | [§8.12](../08-generics-constraints-and-contracts.md#812-runtime-contracts) |
| Origin | A symbolic lifetime denoting a region. | [§15.2.1](../15-ownership-and-lifetime-analysis.md#1521-origin-expressions) |
| Origin parameter | An Origin binder of an associated Type, as in `LentItem(step)`. | [§8.5.4](../08-generics-constraints-and-contracts.md#854-origin-parameters) |
| Origin relation | A required `==` or `outlives` between Origins. | [§15.6.1](../15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness) |
| Overlap | Identical Places, a Place and an inline part of it, or Places that share a byte. | [§3.4](../03-types-and-values.md#34-places-and-storage) |
| Override | A derived body implementing one inherited virtual slot; it adds no lookup layer, overload or public function. | [§6.4.2](../06-declarations-and-containers.md#642-override-target-selection) |
| Owned | Independence from non-static Origins. | [§15.2.3](../15-ownership-and-lifetime-analysis.md#1523-static-and-owned) |
| Pair binder / pair target | In a pair parameter `<s/T>`: `s` / `T`. | [§8.1.1](../08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters) |
| Pair layer | A Type whose outer Semantics is a pair binder. | [§13.5.5](../13-operators-and-assignment.md#1355-follow) |
| Parameter acquisition shape | One acquisition mode for overlapping parameters of one function group. | [§7.4](../07-functions-and-callable-values.md#74-parameter-acquisition-shape) |
| Place / Storage | Selected storage with its Type and capabilities / a region that holds values. | [§3.4](../03-types-and-values.md#34-places-and-storage) |
| Position / Range | A boundary resolving into `[0, L]` / an interval of positions. | [§4.6.1](../04-arrays-indexing-and-slices.md#461-positions), [§4.6.2](../04-arrays-indexing-and-slices.md#462-ranges) |
| `preserves results` | Effect bound: a call conflicts with no Loan of an earlier result of the same requirement. | [§8.11.3](../08-generics-constraints-and-contracts.md#8113-preserves-results) |
| PrimitiveInteger | The compiler-intrinsic Contract of exactly the twelve integer Types. | [§8.7.4](../08-generics-constraints-and-contracts.md#874-primitiveinteger) |
| Project root | The top of a Kotonoha's declaration hierarchy. | [§18](../18-modules-and-dependencies.md#18-modules-and-dependencies) |
| Property | A stored `let`/`var` member or a computed member. | [§11.1](../11-properties.md#111-property-kinds) |
| Punned Place | A transient Place of a Copy, positive-sized Type over bytes that Initialized Places hold. | [§5.2](../05-raw-pointers-and-unsafe-memory.md#52-raw-places) |
| Raw pointer / raw Place | A `raw/T` address / `*p`, `p[n]` or a part of one, designating a Place at its address. | [§5](../05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory), [§5.2](../05-raw-pointers-and-unsafe-memory.md#52-raw-places) |
| Raw storage | Storage, such as an allocation or a dynamic collection's element storage, that declares no Place for the values placed in it. | [§3.4](../03-types-and-values.md#34-places-and-storage) |
| Read / Write / Take | The capabilities of a Place: observe; initialize or replace; extract. | [§3.4](../03-types-and-values.md#34-places-and-storage) |
| Read Type / Value read | A Scalar, position or range Type / the implicit Copy of the terminal value of safe `ref`/`uniq` layers where a read Type is required. | [§3.5.3](../03-types-and-values.md#353-value-read) |
| Reborrow | A borrow derived from an existing borrow. | [§13.5.6](../13-operators-and-assignment.md#1356-borrow-and-reborrow) |
| Receiver Expression | The receiver of a method, accessor or function-value call. | [§3.4.1](../03-types-and-values.md#341-access-paths-and-value-kinds) |
| Receiver shape | The Semantics of a receiver's Type; one per function group. | [§7.3.2](../07-functions-and-callable-values.md#732-receiver-shape) |
| Reference-path selection | Selection through successive safe reference layers, by Types alone. | [§3.4.3](../03-types-and-values.md#343-reference-path-selection) |
| Region | A set of program points; `region(o)` is what Origin `o` denotes. | [§15.2.1](../15-ownership-and-lifetime-analysis.md#1521-origin-expressions) |
| Region splitting | Deriving several child Loans from one parent Loan; exclusive children never overlap. | [§15.6.3](../15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting) |
| Repair candidate | An optional structured edit attached to a diagnostic. | [§23.1.3](../23-compiler-services.md#2313-repair-candidates) |
| Scalar | An integer, floating-point, `bool`, `char` or wrapping integer Core. | [§3.1](../03-types-and-values.md#31-primitive-cores) |
| Sealed | Evidence that a Type is an owner Core other than Never or an open struct. | [§8.7.2](../08-generics-constraints-and-contracts.md#872-sealed) |
| Semantics | A value's representation, ownership, borrowing and access rules. | [§3.3](../03-types-and-values.md#33-semantics) |
| Semantics case | One admitted Semantics per pair binder, under which a generic body is checked. | [§8.15.2](../08-generics-constraints-and-contracts.md#8152-semantics-cases) |
| SourceDocument | One source input of a Kotonoha. | [§18.1](../18-modules-and-dependencies.md#181-modules-and-source-environments) |
| Static Loan | A Loan anchored in static storage. | [§24.3](../24-suspension-and-asynchronous-tasks.md#243-task-calls) |
| Subject Place / Subject mode | The Place `match` or `for` initializes from its Subject / the access it grants. | [§15.1.6](../15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects) |
| Target completion | The Origin bindings given to a runtime-test or checked-cast target. | [§13.6.5](../13-operators-and-assignment.md#1365-checked-casts-and-target-completion) |
| Task / Task tree | The execution of a root that `Async.run` starts or of a child that a Kimi operation starts / a root with its descendants. | [§24.1](../24-suspension-and-asynchronous-tasks.md#241-tasks-and-task-frames) |
| Task call | A call whose arguments begin with `task;`; the only suspension point. | [§24.3](../24-suspension-and-asynchronous-tasks.md#243-task-calls) |
| Task frame | The state of one call of a task function, suspended or not. | [§24.1](../24-suspension-and-asynchronous-tasks.md#241-tasks-and-task-frames) |
| Task slot | The leading `task;` of a parameter list or Function Type. | [§24.2](../24-suspension-and-asynchronous-tasks.md#242-task-slots) |
| Temporary Value / Place | An expression's temporary result / the storage materializing it. | [§3.6](../03-types-and-values.md#36-temporaries) |
| Type | A complete type: Semantics, target and all Origins. | [§3](../03-types-and-values.md#3-types-and-values) |
| Undefined behavior | The result of breaking an unsafe promise. | [§1.3](../01-overview.md#13-normative-status-and-wording), [§5](../05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory) |
| UniqSlice | A non-Copy exclusive contiguous view. | [§4.8.4](../04-arrays-indexing-and-slices.md#484-views-and-uniqslice) |
| Unsafe context / Unsafe Block | Permission for unsafe operations / `unsafe`, which grants it lexically. | [§5](../05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory), [§14.3](../14-control-flow.md#143-do-and-unsafe) |
| User provider Type | A struct or enum with outer `owner` Semantics that is not a built-in numeric Type. | [§13.3.3](../13-operators-and-assignment.md#1333-arithmetic-contracts) |
| Value Context / Discard Context | A context that uses / does not keep an expression's result. | [§14.2.2](../14-control-flow.md#1422-evaluation-contexts-and-body-results) |
| Value kind | Borrow value, owned Place or owned temporary. | [§3.4.1](../03-types-and-values.md#341-access-paths-and-value-kinds) |
| View Target | The public target of an object view. | [§3.3.1](../03-types-and-values.md#331-object-views-and-identity) |
| Virtual slot | An original public function contract with its bound declaration identity. | [§6.4.2](../06-declarations-and-containers.md#642-override-target-selection) |
| Waiting argument | Any argument that is neither Literal nor independently typable, such as an anonymous function, a function reference needing a fixed expected call signature, or a nested call that is not independently typable; never evidence for an outer slot. | [§10.5.1](../10-overload-resolution-and-inference.md#1051-argument-kinds) |
