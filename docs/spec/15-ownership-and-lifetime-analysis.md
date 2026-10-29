# 15. Ownership and lifetime analysis

[Specification index](../SPEC.md)

This chapter decides which uses of Places ([§3.4](03-types-and-values.md#34-places-and-storage)), borrows and Origins are valid. An implementation may elide transfers and temporaries only when values, Place identity and lifetime, Move state, Loans, Origins, destruction responsibility and failures are unchanged; optimization never changes which programs are valid.

## 15.1. Initialization and Move analysis

### 15.1.1. Initialization state

| State | Meaning | Read, borrow, Copy, Move | Write to `let` | Write to `var` |
| --- | --- | --- | --- | --- |
| Uninitialized | Holds no value | Error | Only if never initialized | Initialization |
| Initialized | Holds a value | Type and access rules | Error | Replacement |
| Moved | Its value and responsibility were transferred | Error | Error | Reinitialization |

- A `let` permits one initialization per binding lifetime on each path; a Move does not reset that. State grants no permission by itself.
- A read, borrow, Copy or Move requires the Place to be Initialized on every incoming Runtime Reachability path ([§14.9.3](14-control-flow.md#1493-reachability)). Unreachable code is checked in the assumed-path state of [§14.10.3](14-control-flow.md#14103-unreachable-code); the absence of a path never makes an unusable value usable.

```kimi
var number: i32
if condition
    number = 1
else
    number = 2
inspect(number)           // Valid: both paths initialize number.

let resource: Resource
resource = makeResource()
consume(resource@move)
resource = makeResource() // Error: a let is initialized only once.
```

### 15.1.2. Construction and completeness

A **complete value** has both **construction completion** (its construction finished) and **current completeness** (every stored component is Initialized and complete). A struct's components are its base subobject and its own Fields, with completion tracked per layer; an enum's components are the active Case's payloads, and inactive Cases need no initialization. Every stored Field must be initialized; computed Properties are not components.

Only construction commits completion: the constructor phases ([§6.3](06-declarations-and-containers.md#63-constructors)), Case construction ([§6.5.2](06-declarations-and-containers.md#652-case-references)), Tuple and array construction, `noinit` ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)) and construction that an operation contract states; a punned Place holds a complete value, and relocation keeps a value's completion ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)). No other operation commits or resets it, and an incomplete exit or an Abort never commits. A constructor commits after success is validated and cleanup finishes with the storage alive, before the value is exposed or transferred.

- A Place without construction completion (never initialized, or its whole value Moved) can be initialized only by placing a whole value; writing, reading or borrowing a part of it is an error. The only exception is the construction receiver's own stored Fields ([§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver)); a nested part such as `self.inner.x` is not one of them.
- While a value is incomplete, whole-value reads, Copy, borrows, Move and exposure are errors, also through accessors that take the whole `self`.
- A Partial Move changes only current completeness. Reinitializing every missing part restores it without running a constructor; otherwise the value stays incomplete, and its Initialized parts can still be used and cleaned up. A whole-value Move transfers the construction-completion fact; a whole replacement takes the new value's fact.
- Tuple and array construction places elements in increasing index order. An element gains its initialization and responsibility when its placement completes, and the aggregate commits after all elements are placed. A transfer out of an unfinished construction cleans up the placed elements under [§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order); nothing is rolled back.
- A fixed array needs whole initial construction ([§4.3.2](04-arrays-indexing-and-slices.md#432-initial-construction)). After `noinit`, giving elements valid values before they are read is the programmer's obligation.

```kimi
var pair: (string, i32)
// pair.0 = "a"           // Error: pair was never constructed.
pair = ("a", 1)           // Whole initialization.
let name = pair.0@move    // Partial Move: pair is incomplete.
// let all = pair@move    // Error: incomplete.
pair.0 = "b"              // Reinitializes a Move Path; pair is complete again.
let all = pair@move
```

### 15.1.3. Move Paths and Partial Move

A **Move Path** is a statically trackable path with its own initialization state and responsibility: a Field reached through statically known base-subobject paths, a Tuple element, a fixed-array element at a constant index, or a combination of these. Runtime indices, dynamic containers and user indexers never form Move Paths, and constant propagation and integer proofs add none.

**Constant index.** A constant index is one nonnegative integer literal (any base, digit separators allowed, [§2.6](02-source-and-lexical-structure.md#26-number-literals)) in optional grouping parentheses; it must fit `i32` ([§4.6.2](04-arrays-indexing-and-slices.md#462-ranges)). It forms an element Move Path of `[N of T]` only when its value `n` satisfies `0 <= n < N`; identity uses the value, so `1`, `0x1` and `(1)` name one element. `1 + 1`, `-1`, `3@isize`, names, selections and `^1` are not constant indices. An out-of-range literal forms no path but is not itself an error; its bounds check Aborts if executed ([§17.4.4](17-failure-handling.md#1744-builds-and-compile-time-evaluation)). An operation that needs a Move Path is an error when none exists.

- An element reached through an index that forms no Move Path is Initialized only when the whole array is complete; reading, borrowing, Copying or writing it while any element is Moved or Uninitialized is an error.
- Inside an owned match Subject, selected Case payload positions are Move Paths too ([§15.1.6](#1516-match-and-for-subjects)).
- A Move Path sets tracking granularity, not permission: `@move` transfers an element, or a stored Property under [§11.2](11-properties.md#112-standard-access); a bare read Copies a Copy value.
- A Non-Copy referent reached through a borrow, or a part of it, is never Moved; use `exchange` or `swap` ([§15.7](#157-whole-value-updates)).
- A Field path never bypasses an intervening computed getter.

**Partial Move and `drop`.** Completeness is required immediately before every whole-value use, including a call that receives the whole value, and before a user-defined `drop` runs. An ancestor's `drop` does not forbid a Partial Move; completeness must instead be restored on every path that reaches such a point: normal completion, `return`, `try` propagation, `exit`, `continue`, `yield` and `defer` bodies. If the restoring value comes from `try`, the failure return must be able to destroy the value. Abort does not unwind. Restoration through `defer` or an inner destructor is checked in execution order, and nothing is destroyed twice.

```kimi
var items: [2 of Resource] = [Resource.init(), Resource.init()]
let i: isize = 1
let first = items[0]@move  // Partial Move: items is incomplete.
inspect(items[1])          // Valid: literal index, element 1 is Initialized.
// inspect(items[i])       // Error: a runtime index needs a complete array.
items[0] = Resource.init() // Complete again.
inspect(items[i])          // Valid.
```

### 15.1.4. Consume checks

**Consume** is the static check that a use site may perform an access operation of [§15.1.5](#1515-access-operations-and-acquisition). **Eligibility** is structural: the Place kind, ownership path, Move Path, Field declaration and `drop` restrictions supply the operation. **Legality** is per use site: access is permitted, the target is Initialized on every incoming path and complete, required receiver construction completed, no Loan conflicts, Origins are valid, and the actual ancestors allow it. Constraints prove structural facts only; unknown ones follow generic Access Effects ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)). An ancestor may be incomplete when the target is Initialized, complete and located without whole-value access to that ancestor. A user `drop` can make a path ineligible, so actual ancestors are checked at each use. State, responsibility, completion and completeness are tracked across branches, loops, transfers and `defer`.

### 15.1.5. Access operations and acquisition

A **Movable Place** offers Take. A tracked Place must also be complete and reached by a trackable owned path. A raw Place, and its Fields and elements, offer Take without state tracking; completeness is the author's obligation ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places), [§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places)). Owned roots, permitted inline Fields and Tuple elements, and constant-index fixed-array elements offer Take; borrowed referents, object payloads, static storage, dynamic-collection elements, other indices and hidden Property storage never offer Take; `remove` and owning iterators extract elements under their own contracts.

| Operation | Required conditions |
| --- | --- |
| Copy | Read; complete; the complete Type is Copy |
| Shared borrow | Read; complete; a valid Place and dependencies |
| Exclusive borrow | Read and Write; complete; an exclusively writable lending point (below) |
| Move | Take; for a tracked Place, complete and a trackable owned path |
| Initialize | Uninitialized; the declaration's initialization permission |
| Replace | Write; the old value can be destroyed; no dependency of the old or new value is broken |

Every operation also checks access, Origins and Loan conflicts. The first initialization of a `let` and initialization during construction are dedicated permissions, not Write or Take. A Moved `let` cannot be reinitialized, and Uninitialized storage cannot be borrowed as `uniq/T`. A generic owned value may be Moved whole without a Contract; an unknown `T` is acquired as `T`. A raw Place keeps the obligations of [§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places) and [§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions).

**Lending rule.**

- `@move` requires a Movable Place and transfers even a Copy value: it marks the source Moved and transfers the complete Type, dependencies and responsibility. Moving a reference moves its capability, not its referent. Restoring a Moved `var` needs write permission. An owned temporary transfers without a spelling.
- A new exclusive borrow is written `@uniq` or `@objuniq`, whatever the access path, except for a Receiver Expression, which is acquired implicitly ([§7.3](07-functions-and-callable-values.md#73-receivers)).
- A borrow value needs no spelling: it is Reborrowed in the mode a fixed expected Type requires, or acquired by bare acquisition, never stronger than its own mode or its path's authority. `@ref` or `@uniq` on it borrows its slot ([§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow)).
- Elsewhere (arguments, initializers, assignment sources, results, defaults, elements, payloads) a value is acquired by the common adaptation ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)) when the expected Type is fixed and by bare acquisition ([§3.5](03-types-and-values.md#35-copy-and-move)) otherwise. Getter results are acquired as results, never by moving hidden storage.

**Exclusive acquisition.** Exclusive acquisition, explicit or implicit, of an owned Place, an owned temporary or a reference slot borrowed with explicit `@uniq` requires its lending point ([§3.4](03-types-and-values.md#34-places-and-storage)) to be exclusively writable:

- Writable roots are a `var` local, mutable static storage, an owned temporary, the initialized own Fields of a construction receiver ([§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver)) and the destruction receiver ([§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)).
- A standard projection inherits its root's permission and adds the Property permissions of [§11.2](11-properties.md#112-standard-access) and the Loan restrictions.
- Storage rooted in a `let` binding or an owned-Type parameter, and an owned getter result with its inline parts ([§11.5](11-properties.md#115-getter-results)), cannot be exclusively acquired. A Closure environment binding is not an owned-Type parameter; its `let` or `var` mutability decides ([§7.7.2](07-functions-and-callable-values.md#772-captures)).

The condition applies to the borrowed storage: a referent reached through a returned reference has its own permission, and a `uniq` value held in a `let` or a parameter can still be Reborrowed exclusively.

```kimi
// describe: ref/Array<Task>; consume: Array<Task>; modify: uniq/Resource.
var tasks: Array<Task> = []
tasks.append(Task.init(1))      // Receiver: implicit exclusive acquisition.
describe(tasks)                 // Argument: shared borrow.
var first = makeResource()
// let bad = first              // Error: a bare Non-Copy Place never Moves.
let r = first@uniq              // Exclusive borrows need a spelling.
modify(r)                       // Reborrow of a borrow value in a let.
consume(tasks@move)
```

### 15.1.6. Match and for Subjects

`match E` and `for` evaluate `E` once into an internal **Subject Place** by ordinary acquisition ([§3.5](03-types-and-values.md#35-copy-and-move), [§13.5](13-operators-and-assignment.md#135-explicit-operations)), except that a bare Place is borrowed in place:

| Subject `E` | Subject Place |
| --- | --- |
| A bare Place, including a standard projection of a temporary ([§3.4](03-types-and-values.md#34-places-and-storage)) | A shared borrow of the Place; when the Place stores `ref/T` or `uniq/T`, a Reborrow of that reference in its own mode, bounded to Shared by any shared layer on the path |
| Any other expression (`E@ref`, `E@uniq`, `E@move`, `E@copy`, a temporary, ...) | The value it produces |

The **Subject mode** is the access the Subject Place grants past its safe-reference layers: Shared (`ref`, `objref`), Exclusive (`uniq`, `objuniq`) or ByValue (owned). For a qualifying pair layer it is the weakest mode of the admitted cases ([§13.5.5](13-operators-and-assignment.md#1355-follow)), never ByValue; any other pair layer, such as one admitting Object Semantics, is no safe-reference layer, so a Binding takes its slot by value. A shared layer anywhere on the path bounds the mode to Shared. In a Closure body, a Subject mode is decided with no Call Receiver layer on a path from an environment binding ([§7.7.3](07-functions-and-callable-values.md#773-call-receiver)). The mode never depends on `var`, Copy capability, conformances or the body. An explicit borrow applies to the written slot, so `values@uniq` on `let values: uniq/Array<T>` is an error; write `values` or `values@follow@uniq`. `for` selects its iteration entry from the mode ([§14.6.2](14-control-flow.md#1462-iteration-protocol)); Patterns select Places as in [§14.8.1](14-control-flow.md#1481-patterns).

- The Subject Place is initialized before arm selection. A Property with a custom, computed or required `get` is read once, and the Subject Place is that Temporary Place, with the restriction of [§11.5](11-properties.md#115-getter-results).
- Exclusive mode, also past a pair layer, grants exclusive access only where the lending point can be exclusively acquired ([§15.1.5](#1515-access-operations-and-acquisition)); a binding or `for` entry that needs exclusive access elsewhere is an error, never a shared binding.
- During Pattern testing and guards, [§14.8.3](14-control-flow.md#1483-guards) applies. After arm selection, a borrowed Subject's Loan follows the uses of its bindings, so an arm without a live borrowed binding may replace the original Place.

**Bindings.** A guarded arm is selected only after a true guard whose cleanup completed. The selected arm's locals are initialized left to right:

| Selected Place | Value bound |
| --- | --- |
| A part of an owned Subject or iteration item | The stored complete Type `T` and its responsibility, transferred |
| Reached with shared access | `ref/T`, also for a Copy `T` |
| Reached with exclusive access | `uniq/T` |
| `_` | Nothing; the Subject keeps the responsibility |

- `let` and `var` decide only whether the local may be reassigned; on a borrowed path a `var` binding is a reassignable reference.
- Binding a whole borrowed Subject binds a reference to the original Place; for a Subject that Reborrows a stored `ref/E` or `uniq/E`, that Place is the referent, bound as `ref/E` in a Shared Subject and `uniq/E` in an Exclusive Subject. Binding a whole owned Subject transfers it.
- Through a reference, parts are bound with that path's capability and never Moved out. Unbound parts of an owned Subject stay with the Subject. Payload Copy capability changes no binding Type.

**Lifetime.** Subject temporaries live until the construct finishes ([§3.6](03-types-and-values.md#36-temporaries)). A result or outward transfer value is secured first, then the arm scope is cleaned up (body bindings in reverse order), then the Subject's remaining parts are destroyed. An outward transfer from a guard cleans guard temporaries before the Subject ([§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order)). A borrow through a borrowed Subject depends on the referent and the original Loan, not the internal slot. A new borrow into an owned Subject's payload cannot escape the match.

```kimi
match message                        // Shared borrow; Message is Non-Copy.
    .Write(let text) => inspect(text)  // text: ref/string
    _ => ()
match counter@uniq                   // Exclusive Subject.
    .Count(let n) => n@follow += 1   // n: uniq/i32
    _ => ()

// holder.p has a computed getter returning (s/T, i32); s is owner or uniq.
match holder.p
    (let x, _) => use(x)             // Error: in the owner case x would be uniq/T into the getter result.
```

## 15.2. Origins

[§3.7](03-types-and-values.md#37-origins-and-loans) gives the overview of Origins and Loans.

### 15.2.1. Origin expressions

An **Origin** is a symbolic lifetime: a name, a projection (`x.source`), `static`, or an intersection `(a and b)` (grammar: [§F](appendices/F-syntax-summary.md#appendix-f-grammar)). Each Origin denotes a **region**, `region(o)`: the set of program points where a borrow is valid. Type identity compares Origin bindings by their normalized symbolic form ([§15.2.2](#1522-ordering-and-intersection)); `outlives` and `==` constrain regions.

A concrete Origin (`x`, `self`) is the outer borrow Origin of a parameter, receiver or local (on a raw layer in a struct's or enum's storage, `self` names the value holding it, [§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)); an abstract one (`source`) is a Type slot or an implicitly introduced signature Origin; a projection (`x.source`, `set.source`) selects a slot.

- A safe-borrow value used as an Origin denotes the outer Origin of the borrow it holds, never its storage; a borrow of its slot ([§13.5.5](13-operators-and-assignment.md#1355-follow)) has an inferred Origin. A value without an outer safe-borrow Origin is not an Origin.
- `x.source` projects the slot `source` of `x`'s Type. Local values supply Origins only inside their lexical scope, never in public signatures.

```kimi
struct View<T> {source}
    let data: ref/T during source
    func get(self: ref/Self) -> ref/T during self.source => self.data
```

### 15.2.2. Ordering and intersection

`origin a outlives b` requires `region(a) ⊇ region(b)`; it is reflexive and transitive, and `static` outlives every Origin. `origin a == b` requires equal regions and merges no Loans. `a and b` is the meet: `region(a and b) = region(a) ∩ region(b)`.

Intersections are commutative, associative and idempotent, and `a and b = b` when `a outlives b` is proven ([§15.3.6](#1536-origin-inference)). They are compared after flattening, treating proven-equal Origins as one operand, and removing duplicates and every operand proven to outlive a different remaining operand; a singleton intersection is its operand. Two intersections are equal when the resulting operand sets are equal. Proofs use only the premises of the declaration being checked; when two contracts are compared ([§15.3.7](#1537-contract-comparison)), each is normalized under its own declared premises. Operand order is not observable. Simplifying an Origin expression never removes a distinct input's Loan dependency.

### 15.2.3. `static` and `Owned`

A borrow during `static` must satisfy [§11.8](11-properties.md#118-static-storage). A new safe borrow of mutable static storage has a finite Origin ([§15.6.5](#1565-judging-origin-relations)). In safe code, an Origin whose Loan requirement is `uniq` cannot be bound to `static` ([§15.5](#155-exclusive-origins)). A borrow of a raw Place has a fresh anchor and no upper bound of its own ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)).

`Owned` is a Type Constraint for independence from non-static lifetimes, not ownership or permission to allocate. A complete Type `T` is `Owned` exactly when every Origin in **OwnedOrigins(T)** equals `static`. After expanding aliases and substituting bindings, OwnedOrigins(T) collects:

- the outer Origin, a raw layer's Origin other than `self` included ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)), and the Semantics target (referent, payload or View Target, raw pointee Type);
- all instantiated Type and Origin arguments, including unused and inherited ones ([§6.1.3](06-declarations-and-containers.md#613-inherited-environments-and-declaration-references));
- bases, stored Fields, payloads of every Case, Tuple components, array elements and concrete Closure captures;
- a Function Item's bound arguments, a concrete Closure's fixed signature Origins, and the fixed Origins written in a common Function Type; per-call Origins ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)) are excluded. The hidden environment of a common Function Type, and the payload of a base or runtime-Contract view, were certified at erasure ([§15.8.1](#1581-object-payload-erasure)).

Recursive Types follow [§15.3.5](#1535-variance-loan-requirements-and-phantom-origins). These dependencies imply no Loan; empty containers and unselected Cases do not exempt them, so `raw/(ref/i32 during local)` is not Owned. `a outlives static` proves `a` equal to `static`. An unproven Origin gives Unknown under Finality ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)); a generic definition proves `Owned` only from its declared Constraints ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)).

`Owned` is required by static storage, payload erasure into views, Function Type environment erasure, cyclic-factory payloads ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)) and declared Constraints only. It discharges no acquisition, Loan, destruction-order, unsafe or concurrency check. A failed `Owned` proof, also in a Function Type conversion ([§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)), is a Constraint failure ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)), never a Type mismatch or an Origin relation failure.

### 15.2.4. `Transferable`

`Kimi.Transferable` is a compiler-intrinsic Contract ([§8.7.1](08-generics-constraints-and-contracts.md#871-compiler-intrinsic-contracts)), with no parameters or members. Its written name follows ordinary lookup; only the Kimi declaration has these rules. Conformance is determined structurally, never granted by a user declaration. It decides which values structured workers may receive ([§22.3.6](22-core-execution-and-foreign-functions.md#2236-structured-worker-offload)), the only worker boundary, and which immutable statics a `confined` implementation may read ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)). It requires no `Owned`: borrows of caller storage may qualify, with their Loans kept until join. It supplies no synchronization and permits no concurrent foreign reentry.

- It visits the components of OwnedOrigins ([§15.2.3](#1523-static-and-owned)), including concrete object payloads; a callable signature alone stores nothing. Empty containers and inactive Cases do not exempt their component Types.
- Any reachable `rc`, Weak of an `rc` or raw pointer refutes it, even behind `ref` or `arc`, and so do common Function Types, runtime-Contract views and base-object views.
- The only exception is the raw pointers inside the Kimi collection Types `Array`, `Dictionary`, `Slice` and `UniqSlice` and the standard iterators of [§22.2.2](22-core-execution-and-foreign-functions.md#2222-standard-adapters-and-iterators); the implementation keeps them sound under worker use. Every other raw pointer, including the internal state of `Async.Sender` and `Async.Receiver`, refutes `Transferable`; user code cannot claim the exception.
- Recursive Types follow [§15.3.5](#1535-variance-loan-requirements-and-phantom-origins); a cycle hides no excluded component. Abstract Types need premises ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)).

```kimi
func pass<T>(value: T) -> T
    T is Transferable and Owned
    return value@move

func caller(local: ref/i32)
    let a = pass((1, "x"))                                  // Valid.
    // let b = pass(local)                                  // Error: ref/i32 during local is not Owned.
    // let c = pass(Kimi.Intrinsics.makeRc(Item.init(7)))   // Error: rc refutes Transferable.
```

## 15.3. Origin schemas, names and relations

A **binding set** maps a Type occurrence's schema slots to Origins. There are no function Origin parameter lists and no Type Origin mappings; associated Types may declare Origin parameters ([§8.5.4](08-generics-constraints-and-contracts.md#854-origin-parameters)).

### 15.3.1. Borrow annotations and binding sets

`ref/T during o` sets the Origin of one borrow layer, attached as in [§3.3.2](03-types-and-values.md#332-nested-semantics-and-origin-annotations). `during` is accepted only on `ref`, `uniq`, `objref`, `objuniq`, `raw` and a pair binder; on a pair binder it is a conditional slot, present only in the borrow cases of its admitted set ([§8.1.2](08-generics-constraints-and-contracts.md#812-pair-reconstruction-and-origin-annotations)), and on `raw` it states which storage the pointer addresses ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)). Its argument is one Origin atom (a name, `value.slot`, `set.slot`, `static` or a parenthesized expression, so `during (a and b)`); on `raw` it is no intersection and may be `self`. An annotation creates no Loan, extends no lifetime and performs no acquisition or conversion.

- **Binding sets.** `{name}` after a named Type names that occurrence's set. The Type's schema must be nonempty and known at definition; a duplicate name, empty braces and a set used as a scalar Origin are errors. Braces only name a set: they never apply Origins, positionally or by mapping such as `{source => x}`, and there is no whole-set equality or call-site application such as `f{a}(...)`. Positional Origin application exists only for associated Types ([§8.5.4](08-generics-constraints-and-contracts.md#854-origin-parameters)). Unused set names change no contract.
- **Single-slot binding.** For a Type whose known schema has exactly one slot, `Slice<T> during source` means `Slice<T>{v}` with `origin v.source == source`. It adds no borrow layer; in a signature, an unbound name there is quantified ([§15.3.4](#1534-names-scope-and-quantification)). Other Types reject it.
- **Projection.** `value.source` selects a declared slot of the value's Type, peeling only consecutive safe-borrow layers, never Fields, Type arguments or raw pointers; `set.source` selects a slot of the named occurrence. An unknown slot is an error. A projection reads Type information only.

```kimi
func identity<T>(value: View<T>) -> View<T>{result}
    origin result.source == value.source
    return value@move

func tail<T>(values: Slice<T> during source) -> Slice<T> during source
    return values[1..]
```

### 15.3.2. Type schemas and storage

A struct or enum declares its own Origin slots in a header after its generic parameters: one or more fresh simple names. Duplicates, empty braces and redeclared inherited names are errors. Without a header the Type has no own slots, which does not make it Owned. Groups and Contracts declare no slots.

- **Names in storage** (Fields, payloads, bases) resolve to a declared slot or a visible enclosing Origin ([§15.3.4](#1534-names-scope-and-quantification)), or, on a raw layer, to `self` ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)); an undeclared name is an error. Nested Types inherit enclosing slots lexically.
- **Fragments** follow [§6.1.2](06-declarations-and-containers.md#612-container-fragments), including the shared Constraint region and generated members; enums cannot be split.
- **Stability.** Slot names are API ([§15.3.7](#1537-contract-comparison)). A slot that no storage uses stays a Phantom Origin ([§15.3.5](#1535-variance-loan-requirements-and-phantom-origins)).
- **Storage completion.** Every stored borrow layer and aggregate slot is completed from the Type's public Origins, `static`, complete Type arguments, annotations and relations; initializers never infer it, and no hidden slot is created. A Field-local set is visible only in that Field and its clauses. Remaining Field and payload conditions must follow from the Type's public premises and well-formedness; extra premises belong in its Constraint region.

```kimi
struct Renamed<T> {buffer}
    let data: ref/T during buffer
    let other: ref/T during souce       // Error: souce is not declared.

func forward<T>(value: Renamed<T>) -> Renamed<T>{result}
    origin result.source == value.source   // Error: Renamed has no slot source.
    return value@move
```

### 15.3.3. Declaration-attached relations

`origin a == b` and `origin a outlives b` each state one relation; several clauses are conjunctive, and there are no chains, disjunctions, negations or runtime tests. A function's or Type's clauses are in its leading region ([§7.5](07-functions-and-callable-values.md#75-function-constraints)). Fields, locals, enum Cases, associated-Type specifications and Container aliases attach clauses indented under the declaration, before any accessors or bodies; every name must be visible there.

| Owner | Obligation |
| --- | --- |
| Public function or Type | Check the definition for every admitted binding; prove the substituted premises at each use |
| Field, payload, associated-Type specification | Prove remaining conditions from the enclosing public contract |
| Base Type | Publish extra premises in the Type's Constraint region |
| Local or Container alias | Check against established evidence and the initializer; assume nothing |

A closed condition that depends on no declaration parameter is a definition-time obligation; a false one is an error.

### 15.3.4. Names, scope and quantification

In a named callable signature, an unbound simple name in a borrow annotation introduces a universally quantified Origin, also when it appears only in the result. Relations and local annotations only reference names. Specializations and inherited stored accessors keep the original contract.

| Name owner | Visible in |
| --- | --- |
| Callable signature Origin or set | Signature, clauses and body |
| Type slot | Type declaration and inherited members |
| Field, Case or associated-specification set | That declaration and its clauses |
| Base occurrence set | The Constraint region of the fragment that writes the base clause ([§6.1.2](06-declarations-and-containers.md#612-container-fragments)) |
| Associated-Type Origin parameter | Its right-hand side, formation Type and clauses ([§8.5.4](08-generics-constraints-and-contracts.md#854-origin-parameters)) |
| Local set | Its Type, clauses and the following scope |
| Container alias set | Its clauses and the document's alias scope |

- Lookup searches scopes inside out and stops at the first scope with an Origin-context candidate; a same-scope conflict among value, Origin and set roles is an error, and a wrong-role match never falls back outward. Bare Fields are no candidates without a receiver. Type-parameter names are a separate namespace.
- A new Origin or set name cannot hide a visible Origin, set, parameter or local; duplicates are errors, but mutually invisible Field-local names may repeat. Resolution does not depend on declaration order.
- A named nested function's signature may name visible Origins of enclosing functions. Such a name is a fixed Origin of the nested function, not a new universal one, and captures nothing at run time; a function value of the nested function depends on those Origins like any value whose Type names them.

**Nested signatures.** Function Types, Callable and anonymous functions introduce no named Origins: they keep per-call direct-input quantification and their own result elision ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)), and their set names belong to the enclosing declaration. Each nested aggregate input slot is fixed by a complete Type or an outer Origin expression, except that in a local's initialized Type and an anonymous function's parameters an omitted one, or an omitted Origin of a nested borrow layer, is a body inference variable, fixed once ([§15.3.6](#1536-origin-inference)). Inner per-call Origins cannot escape their binder.

```kimi
func useView<T>(x: View<T>, callback: (View<T>{c}) -> ())
    origin c.source == x.source   // Fixed by the outer call.
    callback(x@move)

func use(source: uniq/i32, bound: ref/i32) -> i32
    origin source outlives bound
    func pass(value: uniq/i32 during bound) -> uniq/i32 during bound => value
    let child = pass(source)      // bound is a fixed Origin of pass.
    child@follow = 42
    return source@follow          // Valid: child has no later use.
```

### 15.3.5. Variance, Loan requirements and Phantom Origins

**Variance.** A safe borrow is covariant in its outer Origin, and a raw layer in its Origin ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)). `uniq`, `obj`, `objuniq` and `raw` are invariant in their target; every other Semantics is covariant in its target, because `ref`, `objref`, `rc` and `arc` grant no mutation ([§15.6.2](#1562-place-overlap-and-conflicts)). In declaration variance a pair layer counts as covariant; in a generic body its target is invariant when an invariant Semantics is admitted. Function parameters reverse polarity. A Type derives its variance from its storage; there are no variance annotations.

**Loan requirements.** Each slot has a requirement `none < ref < uniq`, from its shared or exclusive uses; nested uses propagate the stronger. A conditional slot ([§8.1.2](08-generics-constraints-and-contracts.md#812-pair-reconstruction-and-origin-annotations)) is a use only in the admitted borrow cases of its binder, shared in the `ref` and `objref` cases and exclusive in the `uniq` and `objuniq` cases, so a slot's requirement and Phantom status are judged per admitted Semantics case of the declaration's pair binders; an instance has those of the case its arguments select ([§8.15.2](08-generics-constraints-and-contracts.md#8152-semantics-cases)). A raw layer's Origin is no use; a `during self` pointee counts as stored content of its Type ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)). It names the caller-side Loan to keep and grants nothing. Equal or shortened Origins never merge Loans or create exclusive access.

**Fixed points.** Variance, Loan requirements, OwnedOrigins and `Transferable` are computed once per declaration (Loan requirements and Phantom status once per admitted Semantics case of its pair binders), in terms of its own Type parameters and slots, never by expanding instantiations. Each is the least fixed point from the bottom value (unused, `none`, empty set, no exclusion), so recursive Types, including non-regular ones such as a Field `rc/Nest<(T, T)>?`, terminate.

**Phantom Origins.** A slot is **Phantom** when no chain of stored Fields, including slots of nested Types bound to it, reaches a stored safe reference or a `Loan<T>` Field. It keeps its dependency but grants no validity, Loan, Copy or access; its requirement is `none`, and it is invariant. Variance stays one per declaration: a slot is invariant as Phantom only when it is Phantom in every admitted case.

**`Kimi.Loan<T>`** is a zero-sized compiler-managed struct that expresses a dependency without storage. `T` must be a borrow Type with outer `ref`, `uniq`, `objref` or `objuniq`. A `Loan<T>` Field counts as storing a `T` for all slot properties; it is Copy when `T` is, has no address and grants no access. `Loan<T>.init(value: T)` is safe and keeps the borrow's dependency; destroying it ends only its own responsibility, and the Loan stays live while the values that carry it are used ([§15.6](#156-borrow-checking)). `Slice<T>` ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)), `UniqSlice<T>` (invariant, [§4.8.4](04-arrays-indexing-and-slices.md#484-views-and-uniqslice)) and `Array<T>` ([§4.5](04-arrays-indexing-and-slices.md#45-array-ownership)) take slot properties from their own rules; `Weak<S>` counts as storing an `S` ([§3.2.2](03-types-and-values.md#322-weak-references)).

```kimi
struct Window<T> {source}               // Covariant in T and source.
    let loan: Loan<ref/T during source>
    let data: raw/()

struct Node<T> {a}
    let value: ref/T during a
    let next: Option<rc/Node<T>{n}>
        origin n.a == a                 // Recursion adds nothing; a is covariant.
```

```kimi
struct Holder<s/T> {a}
    s is owner or uniq
    let item: s/T during a          // a is uniq in the uniq case, none and Phantom in the owner case.

func cached(h: Holder<i32> during static) => ()                   // Valid: the owner case stores no reference.
// func lent(h: Holder<uniq/i32 during r> during static) => ()   // Error: a is uniq in the uniq case (§15.2.3).
```

### 15.3.6. Origin inference

Inference variables are placed only at unresolved positions, constrained by variance, relations and uses.

| Position | Candidate |
| --- | --- |
| Covariant | Unless fixed by equality, the meet of all upper bounds |
| Invariant | The proven equality; never a meet |
| Contravariant | A lower bound proven to outlive every other lower bound |

Variables connected by a cycle of `outlives` relations are equal and solved as one. A variable at both covariant and contravariant positions is solved as invariant. Each remaining variable takes its candidate from its row using only bounds of that row's kind; its other bounds are then checked against the candidate. A variable's bounds are followed through other variables to fixed or finite Origins ([§15.6.5](#1565-judging-origin-relations)) and `static`. If none is reached, the variable has no candidate. If its row has no candidate, the variable is a local region in an executable body ([§15.6](#156-borrow-checking)), never replaced by `static`; elsewhere an annotation is needed. Inference is judged after overload selection and never makes a candidate inapplicable.

The solver uses exactly these rules: reflexivity; transitivity; `static` outlives every Origin; substitution of `==`; the laws of [§15.2.2](#1522-ordering-and-intersection); `a outlives (a and b)` and `b outlives (a and b)`; and `(a and b) outlives c` exactly when `a outlives c` and `b outlives c`. A meet on the right is never split into separate requirements, but `x outlives a` proves `x outlives (a and b)`. Entailment does not depend on the order or repetition of premises and forms no Origin beyond the queried expressions, the premises and their subexpressions. Unknown is distinct from Refuted and is decided under Finality ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)).

```kimi
func choose<T>(x: ref/T, y: ref/T) -> ref/T  // Result Origin: (x and y).
    return x                                 // Valid: x outlives (x and y).
```

### 15.3.7. Contract comparison

Well-formedness is a definition premise and a use-site obligation ([§15.6.1](#1561-origin-relations-and-well-formedness)). Slots have declaration-bound identity.

- **Equality** compares normalized structure, quantification, conditions, bindings and guarantees, each side normalized under its own premises. Origin or set spelling alone distinguishes neither overloads nor specializations. Slot names are API.
- **Compatibility** admits every call the requirement allows and gives at least its result guarantees. Required universal Origins are rigid; only the implementation's instantiable call Origins may be solved. Inputs are checked as `required <: implementation` and results as `implementation <: required` ([§3.8](03-types-and-values.md#38-type-relations-and-operations)), and implementation conditions are proven from the requirement's premises. Receivers, environments, authority and Loans are checked separately, in every admitted Semantics case.

Origins have no run-time representation: they add no runtime arguments or lifetime tags.

## 15.4. Origin completion and elision

A contract is completed once, at definition: inherited complete Types are kept, names resolved, annotations and relations normalized, and the remaining omitted positions completed by the rules below. Naming a binding set does not suppress omission, and a named function's contract is never inferred from its body.

### 15.4.1. Explicit result relations

For an ordinary callable's aggregate result slots:

1. Keep established complete or inherited bindings and positions fixed by an annotation or by equality with an existing Origin expression.
2. Universally quantify the unbound result slots that remain in nontrivial `outlives` relations after the explicit `origin` clauses are normalized, subject to those relations.
3. Apply result elision ([§15.4.3](#1543-result-defaults)) to the remaining slots. Equalities that connect only unresolved result slots form one class, and every position in a class must receive the same default. If the defaults differ, or a position has no default, the declaration is an error and needs an explicit result relation.

Well-formedness alone never makes an omitted slot universal. Tautologies such as `static outlives x` and `x == x` change neither omission nor quantification.

### 15.4.2. Position rules

After aliases, grouping and redundant owner prefixes are normalized, an omitted Origin of a safe-borrow layer or aggregate slot is completed by its position; a raw layer's Origin is never completed: without `during` it states none ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)).

| Position | Omitted Origin |
| --- | --- |
| Direct borrowed parameter or receiver | An independent Origin for that input's outer layer |
| Aggregate in a callable input | An independent universal Origin per unresolved slot |
| Function result | [§15.4.3](#1543-result-defaults); an omitted whole result Type is Unit |
| Local with an initializer | Inferred ([§15.4.4](#1544-locals-and-independent-type-expressions)) |
| Local without an initializer, Field, payload | Must be complete from explicit information |
| Type in a Constraint, outside a Function Type | Must be complete from explicit information |
| Static storage | Requires `Owned` and [§11.8](11-properties.md#118-static-storage); shared layers and `none`/`ref` slots default to `static`; `uniq` is an error |
| Accessor or specialization | The inherited contract first; getter results use the getter receiver |
| Adaptation Target, test or cast target | From the operand ([§13.5.1](13-operators-and-assignment.md#1351-forms-and-adaptation-targets), [§13.6.5](13-operators-and-assignment.md#1365-checked-casts-and-target-completion)) |

Input completion recurses through Type arguments, Tuple and array elements and borrow targets, not through Fields, complete Types or callable boundaries; distinct inputs and slots stay independent. An omitted Origin of a borrow layer nested in an input is an error; only the outer layer of a direct input and aggregate slots are completed. Constructors keep the Type's result contract. An original `s/T` ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)) keeps its bindings, and a reconstructed `s/U` keeps conditional slots ([§8.1.2](08-generics-constraints-and-contracts.md#812-pair-reconstruction-and-origin-annotations)).

### 15.4.3. Result defaults

Each remaining omitted result Origin is completed independently:

1. With direct borrowed inputs, including borrowed receivers, use the meet of all their outer Origins, never aggregate-internal Origins.
2. Otherwise a shared layer defaults to `static`, and an exclusive layer needs an explicit contract.
3. Otherwise an aggregate slot defaults to `static` only when every input Type is provably `Owned` (true with no inputs) and its Loan requirement is `none` or `ref`; else it needs an explicit contract.

Conditional direct-borrow inputs are evaluated in every admitted Semantics case. Elision never adds an `Owned` Constraint. For `func f<T>(x: ref/T? during a) -> ref/T`, `x` is not a direct borrowed input, so the result is `static`.

```kimi
func first<T>(x: ref/T) -> ref/T => x                 // during x
func choose<T>(x: ref/T, y: ref/T) -> ref/T => y      // during (x and y)
func view<T>(x: ref/T) -> View<T> => makeView(x)      // source: x
func invalid<T>(x: View<T>) -> View<T> => x@move      // Error: a result relation is required.
```

### 15.4.4. Locals and independent Type expressions

A local's declaration fixes its Type and inference variables; later uses constrain them without reopening inference, and later assignments must fit the same contract. Without an initializer, equalities or established proofs must fix every slot. Unresolved obligations are decided under Finality ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)); uncertainty never becomes an invented `static` or an erased dependency. Attached clauses are collected with the Type annotation; in them, `view.source` names the declared Type's slot. A Type expression in a body is bound by attaching relations to its declaration; set names in an initializer's written Type expressions belong to the local declaration, and an intermediate Container qualifier is named `(Outer<T>{o}).Inner<U>`. Leading function clauses cannot name body Type occurrences. A Container alias's clauses cannot reference runtime values or another file's bindings.

Ordinary storage keeps complete Types and actual Loans through acquisition, Move, Copy, calls and destruction, with no blanket `Owned` requirement; heap placement never extends a referent's lifetime.

## 15.5. Exclusive Origins

An exclusive borrow is unique because of the conflict rules ([§15.6.2](#1562-place-overlap-and-conflicts)) and Reborrow containment ([§15.6.3](#1563-reborrowing-and-region-splitting)), not because of its Origin. Hence a `uniq` result reached through an exclusive receiver must lie within that receiver's Origin, and safe code cannot bind an Origin whose Loan requirement is `uniq` to `static`; violations are errors.

The signature `(self: uniq/Self) -> uniq/T during self.source` is valid, but a body cannot satisfy it by Reborrowing a stored Field through `self`: such a Reborrow is contained in `self`'s Origin, so returning it is an error at the return. Raw storage splitting implements this signature with an unsafe `uniq` borrow of a raw Place ([§15.6.3](#1563-reborrowing-and-region-splitting)).

```kimi
struct View<T> {source}
    let data: ref/T during source

    func get(self: ref/Self) -> ref/T during self.source
        return self.data          // Copy of the stored reference keeps source.

struct UniqView<T> {source}
    let data: uniq/T during source

    func getUniq(self: uniq/Self) -> uniq/T during self
        return self.data          // Reborrow through self; self stays borrowed.

    func intoUniq(self: Self) -> uniq/T during self.source
        return self.data@move     // Consumes the view, so the capability is not reused.

    func detach(self: uniq/Self) -> uniq/T during self.source
        return self.data          // Error: a Reborrow through self stays within self.
```

## 15.6. Borrow checking

A **program point** is a position immediately before or after an operation. A static Field's storage ([§22.4.4](22-core-execution-and-foreign-functions.md#2244-static-storage-keys)) is a Place root like a local ([§15.6.2](#1562-place-overlap-and-conflicts)); non-standard accessors are call boundaries. Local regions are inferred, signature Origins denote universal regions, and `static` is the maximum region.

**Loans.** A **Loan** has a Place, named by its access path ([§15.6.2](#1562-place-overlap-and-conflicts)), and a mode, `ref` or `uniq`. It is identified by its Borrow site and Semantics case ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)): every execution of one site in one case forms the same Loan. Implicit Borrows (Receiver Expressions, argument adaptations, comparison inspections, capture entries) have the site of their operation; a call's caller-side Loans ([§15.6.4](#1564-calls)) have the call's; the Borrows of a default evaluated at a call ([§7.2.3](07-functions-and-callable-values.md#723-default-evaluation)) are identified by that call and the default's operation. The Loans a body receives through its parameters' Origins have no access path in it; they are checked where the body is called, against what the call may reach ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)) and its effects on static storage ([§15.6.4](#1564-calls)). A Loan's **anchor** is the lending point of the borrow that creates it ([§3.4](03-types-and-values.md#34-places-and-storage)).

- The values that carry a Loan are the borrow itself, its Copies, references derived from it, and every aggregate, Closure or storage that holds one of these. After a call, its results and the writable storage its public contract lets it write carry the Loans of the arguments they may retain ([§15.6.4](#1564-calls)).
- **Addresses.** A raw pointer formed by `P@raw` **addresses** the holder at the root of `P` when `P`'s path dereferences no borrow and no raw pointer; otherwise it carries what a Borrow of `P` would carry ([§15.6.3](#1563-reborrowing-and-region-splitting), and past a raw pointer [§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)), without the Loan that Borrow would form. A raw pointer read from a raw Field that states `self` reaches what `@raw` of the Field's storage reaches ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)). An address is carried as a Loan is, by Copies, displacements, conversions between raw pointer Types and every value or holder that holds one, but it is no Loan: it conflicts with nothing and grants no authority ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)).
- **Holders** are locals, parameters and temporaries. A holder carries the Loans of the values stored in every part of it. A value read, Copied or Moved from a part carries every Loan the holder carries, and so does a Borrow through a reference stored in a part, as its parent Loans ([§15.6.3](#1563-reborrowing-and-region-splitting)); a destruction of a part that may observe a Loan ([§15.6.6](#1566-destruction-lifetime-checking)) uses the whole holder. A holder that is completely replaced, by `=` to the whole holder or by the start or end of its storage, no longer carries the Loans of its old value. Writing or Moving a part ends none of them, and a written value's Loans are added to the holder. Storage the body does not own (next item) follows the holder as Loans do: a value derives from a reference that reaches such storage when the value is a Copy or Reborrow of one, a value a call may retain from one, a read of a borrowed argument included ([§15.6.4](#1564-calls)), or a value read from a part of a holder that holds one, directly or through a reference to that holder.
- A write through a reference, a non-path index or a dynamic container adds the written value's Loans to every holder it may reach and ends none. A call writes the storage its contract lets it write the same way, so `replace`, `exchange` and `swap` ([§15.7](#157-whole-value-updates)) completely replace no holder; `replace` ends Loans only as [§15.6.3](#1563-reborrowing-and-region-splitting) states. A reference reaches the holders named by those Loans it carries whose access paths have no dereference ([§15.6.2](#1562-place-overlap-and-conflicts)), its own and those of the references it was Reborrowed through, and the holders it addresses. It also reaches **storage the body does not own** when it comes from a parameter or capture, from static storage, from a part of a call result that its contract ties to no argument, or from a raw layer without an Origin that a call writes, in its result or in storage ([§15.6.4](#1564-calls)), or derives from such a reference (above); a raw pointer that reaches no holder by these rules, such as a `Raw.allocate` result, reaches only such storage. A value stored there may be used after every point. A read through a reference carries the Loans of what it reaches. A compound assignment, increment or decrement writes its destination through its reservation ([§13.7.2](13-operators-and-assignment.md#1372-compound-assignment)), so it ends none of the Loans the destination carried.
- A Loan is **live** at a point P when a value that carries it may be used after P, including an observation by its destruction ([§15.6.6](#1566-destruction-lifetime-checking)). A value is also used wherever a value that carries a Loan on a Place overlapping the first value's holder, or an address of that holder, is used. An operation at P is checked against the Loans carried before P by the values used after P, other than those P replaces, and by the values P stores into a holder used after P or into storage the body does not own. These values decide, never a Loan's identity: a Borrow is not checked against the Loan it forms, which no value carries before it, but it is checked against an earlier execution of its own site that such a value carries. The activation of a reservation ([§15.6.7](#1567-call-reservations)) is checked against the values live into its call other than its own argument.
- Liveness is a may-analysis: after a join a Loan is live if it is live on any incoming path, and loops use the least fixed point.
- Origin regions and `outlives` relations only check that lifetimes are compatible; they never keep a Loan live.

```kimi
var a = 1
var b = 2
var r = a@ref
r = b@ref       // r no longer carries the Loan on a.
a = 3           // Valid.
inspect(r)
```

A complete write through a reference ends none of the referent's earlier Loans:

```kimi
var a = 1
var b = 2
var slot: ref/i32 = a@ref
let r = slot@uniq
r@follow = b@ref    // A write through r: slot may now carry b's Loan, and a's Loan is not ended.
// a = 3            // Error: slot may still carry a's Loan and is used below.
inspect(slot)
```

### 15.6.1. Origin relations and well-formedness

**Liveness.** If a value whose Type contains Origin `o` may be used after P, then P belongs to `region(o)`. A raw layer's Origin is exempt: it constrains no region ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)).

**Well-formedness.** For every safe borrow layer `B/V during o`, where `B` is `ref`, `uniq`, `objref` or `objuniq`, or a pair binder in its admitted borrow cases, every Origin in OwnedOrigins(V) ([§15.2.3](#1523-static-and-owned)) must outlive `o`. For an object borrow, `V` is the View Target with its Type and Origin arguments. The rule is judged per admitted Semantics case: a pair layer's outer Origin is part of OwnedOrigins only in the cases of its binder that have one ([§15.6.5](#1565-judging-origin-relations)). Every written or inferred Type must be well-formed. A definition may assume that the Types of its signature (parameters, receiver, result and `Self`) are well-formed; each call proves this for the instantiated Types ([§15.6.4](#1564-calls)). A nested signature's Types ([§15.3.4](#1534-names-scope-and-quantification): a Function Type's, a Callable's or an anonymous function's), its inputs and its result, are its own: the declaration or body that writes it neither assumes nor proves them well-formed, and each call through it proves this for the instantiated Types.

**Origin relations.** A fit of a value to a destination Type ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)) has a structural part, normalized Type identity with Origins treated as equal ([§3.8](03-types-and-values.md#38-type-relations-and-operations)); only its failure is a Type mismatch, and then no Origin relation forms. Once it holds, each Origin position yields a relation by its variance: `outlives` at covariant and contravariant positions, `==` at invariant ones. Declared relations, relations substituted at a call and well-formedness are Origin relations too; the static-storage rule of [§11.8](11-properties.md#118-static-storage) is a fit to `static`. Applicability ([§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability)) and expected-result filtering ([§10.3](10-overload-resolution-and-inference.md#103-expected-results)) use only structural parts; Origin relations are judged after selection ([§15.6.5](#1565-judging-origin-relations)) and never change it. When equalities make a call's fresh Origin equal to several fixed Origins, those must be equal.

Constraints whose proof involves Origins (`Owned`, Callable, Type identity, [§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)) are separate requirements that take part in applicability. The Origin part of a whole-contract comparison ([§8.6](08-generics-constraints-and-contracts.md#86-conformance), [§8.13](08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization), [§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)) is judged under [§15.3.7](#1537-contract-comparison) from premises alone, so it is Proven or Unknown; Unknown is an error of the Proof category, or a Constraint failure inside a Callable Constraint.

**Errors.** A Refuted Origin relation is an error. One still Unknown under Finality ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)) is an error of the Proof category: not proven, not false. A failed relation adds no constraint to region inference, so it causes no Loan conflict or result mismatch of its own. Clauses and well-formedness a body must establish are obligations, never premises.

```kimi
// Holder<T> is a struct with the Field value: T.
func get(b: objref/Holder<ref/i32 during c> during o) -> ref/i32 during o
    return b.value   // Valid: b's Type is well-formed, so c outlives o.
```

### 15.6.2. Place overlap and conflicts

Places overlap as [§3.4](03-types-and-values.md#34-places-and-storage) defines. Loan checking compares the access paths ([§3.4.1](03-types-and-values.md#341-access-paths-and-value-kinds)) of an operation and a Loan step by step. A path starts at a root (a local, parameter or temporary, the keys of a static Field, or the fresh anchor of a raw Place borrow) and steps through inline parts (Fields, base subobjects, Tuple elements, indices and Case payloads), **own** steps and **dereferences**. A dereference is a step through a `ref`, `uniq`, `objref` or `objuniq` reference, **shared** through `ref` or `objref`; examples write it `.*`, so `r.*.x` is the Field `x` of `r`'s referent. A step into the payload of an `obj`, `rc` or `arc` handle is an own step, not a dereference: the payload's paths extend the handle's, whatever access [§3.4.1](03-types-and-values.md#341-access-paths-and-value-kinds) grants through it. A dereference matches only a dereference, and an own step only an own step. Two paths overlap unless these rules prove them disjoint:

| Paths | Result |
| --- | --- |
| Identical paths, or a path and an extension of it | Overlap, reached as stated below |
| Paths from distinct roots, through dereferences included | Disjoint: the Loans each reference carries cover its aliases ([§15.6](#156-borrow-checking)) |
| Distinct inline Fields, Tuple elements or constant indices of one aggregate, and their extensions | Disjoint |
| Payloads of distinct Cases of one enum, and their extensions | Disjoint, although they may share bytes: a Case changes only by a write of the enum, which reaches every Loan on its payloads |
| A raw Place borrow's fresh anchor ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)) and a Place not derived from it | Not compared; an unsafe obligation ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)) |
| Anything else, such as indices other than distinct constant ones | Unproven: an operation that needs a proof is an error |

- All keys of one static Field are one root, whatever the enclosing Type arguments; keys of different Field declarations are distinct roots ([§22.4.4](22-core-execution-and-foreign-functions.md#2244-static-storage-keys)).
- A Loan whose path is a prefix of an operation's path is reached by the operation, so an access through a reference reaches a Loan on the Place that holds it. A Loan whose path extends the operation's path is reached by a read, write or destruction only when no dereference lies between them; a Move or Borrow reaches it across dereferences, except the Borrow of a target that the operation destroys in place ([§15.7](#157-whole-value-updates): `replace`), and so does a destruction that may observe a Loan ([§15.6.6](#1566-destruction-lifetime-checking)), up to the first shared dereference.
- Runtime comparisons such as `i != j` and integer proofs prove nothing. Array-derived Slices keep the whole-array footprint ([§4.8.1](04-arrays-indexing-and-slices.md#481-slice)). Accessor calls other than standard ones use their whole receiver.

Each operation is checked against every Loan it reaches among those checked at its point ([§15.6](#156-borrow-checking)):

| Operation | Live `ref` | Live `uniq` |
| --- | --- | --- |
| Read, create `ref` | Allowed | Error |
| Write, Move, create `uniq`, destroy the Place | Error | Error |

**Shared access grants no mutation.** A Place is written, Moved, exclusively borrowed or destroyed only through a direct path or an exclusive reference ([§3.4](03-types-and-values.md#34-places-and-storage)), or by raw access within its authority ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)). Past a shared layer (`ref`, `objref`, `rc`, `arc`) only reads and shared borrows are possible; through a raw pointer read there, or past a raw pointer whose Origin keeps only shared Loans, any other access is undefined behavior rather than an error ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)); reference counts and Weak tables are not Places.

### 15.6.3. Reborrowing and region splitting

Borrowing through an exclusive borrow creates a child Loan anchored on the referent; its values also carry the Loans of every reference dereferenced to reach it, its parent Loans. A Borrow past a shared dereference ([§15.6.2](#1562-place-overlap-and-conflicts)) forms no Loan of its own; it derives only from the reference of its last shared dereference and the references it dereferences after that one, and carries their Loans. While the child is live, access through the parent is suspended, and no prefix of the child's access path, such as the Place that holds the parent, can be Moved or borrowed, except shared while the child is shared or as a target that the operation destroys in place ([§15.7](#157-whole-value-updates): `replace`).

Destroying in place a Move Path ([§15.1.3](#1513-move-paths-and-partial-move)) ends every Loan whose access path it exactly prefixes, once the destruction of the old value there is checked ([§15.6.6](#1566-destruction-lifetime-checking)): by `=`, at the end of its storage, or as a `replace` target. The Move Path's steps are compared one by one, so a constant index matches only the same constant and never a runtime index; the access path may continue past it with any steps, dereferences included. No value carries an ended Loan any longer, and the values that carried an ended child keep its parent Loans. So a Reborrow of a parameter may be returned, and replacing an outer reference ends the children formed through its old value, whose parent Loans still protect the old referents. Any other child ends when no value that carries it may be used. The child is contained in the Origin of the parent and of each safe borrow through which the parent is reached, such as a receiver ([§15.5](#155-exclusive-origins)) or a Closure's call receiver ([§15.8.2](#1582-closure-dependencies)), up to and including the first shared layer. A shared Borrow through a shared reference is thus contained only in that reference's Origin.

```kimi
// Tree has `var value: i32` and `var children: Array<Tree>`.
func deepest(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root
    while cursor.children.length > 0
        let next = cursor.children[0]@uniq  // Keeps indexUniq's receiver Loan: path cursor.*.children.
        // cursor.children.clear()          // Error: reaches the Loan that next keeps, and next is used below.
        cursor = next                       // Assigning cursor ends that Loan; cursor keeps root's Loans.
    return cursor
```

Moving or exchanging a prefix of a live child's access path, or borrowing it other than shared while the child is shared, is an error; `replace` ends the child as `=` does:

```kimi
var x = 1
var y = 2
var r = x@uniq
let c = r@follow@uniq                                       // A child Loan: path r.*.
// let moved = r@move                                       // Error: Moves r while c is live.
// let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq) // Error: borrows r exclusively while c is live.
Kimi.Intrinsics.replace(r@uniq, with: y@uniq)               // Valid: destroys r's old value in place, ending c's Loan.
c@follow = 3                                                // c keeps the parent Loan on x.
```

Replacing an outer reference ends a child formed through two dereferences in the same way, while its parent Loans protect the old route; a Borrow that would keep the old route beside the child is rejected while the child is live:

```kimi
var x = 1
var z = 3
var slot = x@uniq
var slot2 = z@uniq
var rr = slot@uniq
let c = rr@follow@follow@uniq   // A child Loan: path rr.*.*, with parent Loans on slot and x.
// let w = rr@follow@ref        // Error: Borrows rr.*, a prefix of c's path, while c is live.
rr = slot2@uniq                 // Destroys rr's old value in place, ending c's Loan.
rr@follow@follow = 7            // Valid: writes z through the new value.
// slot@follow = 2              // Error: c keeps the parent Loan on slot.
c@follow = 1                    // Writes x.
```

**Region splitting** derives several child Loans from one parent the same way. Exclusive children and the parent's remainder must be pairwise non-overlapping, by [§15.6.2](#1562-place-overlap-and-conflicts) or by the unsafe promise of a `uniq` borrow of a raw Place; shared children may overlap. A Reborrow through a child stays within that child. A value a call returns from its receiver's Origins, such as an item split from an Iterator or remainder, carries what the per-argument retention of [§15.6.4](#1564-calls) gives it. Destroying the Iterator or remainder does not end published child Loans. A library splits raw storage in ordinary code: the remainder keeps the parent Loan in a `Loan<uniq/… during source>` Field and addresses the storage by a raw Field `during source` ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)), each child is a `uniq` borrow of a raw Place returned `during state.source`, and keeping the promise of [§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places) for every child is the unsafe obligation.

```kimi
// Take is the contract of §8.11.1.
func takeFirst<S>(h: uniq/(uniq/S during s, i32)) -> Option<S.Item>
    S is Take
    var r = h@follow@uniq
    let first = r.0.take()  // Returned from the receiver r.0's Origins: keeps what a read through it carries (§15.6.4), not r's Loan on h.*.
    h.1 = 5                 // Valid: no live value carries r's Loan.
    return first@move
```

A result may be kept across later calls on the same receiver when its Type does not depend on its call's receiver Loan and no later call's effects conflict with the Loans it keeps, whether the callee is concrete or abstract ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)).

### 15.6.4. Calls

A call is checked as follows:

1. creates fresh regions for the callee's abstract Origins;
2. instantiates the parameter Types and fits the arguments; a pair slot bound to an abstract original `s/T` keeps its Origins, and the conditional slot of `s/U` binds the argument's outer-Origin slot ([§8.1.2](08-generics-constraints-and-contracts.md#812-pair-reconstruction-and-origin-annotations));
3. instantiates the result Type and proves the substituted declared relations and the well-formedness of every instantiated parameter and result Type, solving the fresh regions by [§15.3.6](#1536-origin-inference);
4. collects its dependencies; a result that names a borrowed input's Origin, including a Closure's call receiver ([§7.7.3](07-functions-and-callable-values.md#773-call-receiver)), also depends on every Origin in OwnedOrigins of that input's referent Type;
5. creates the caller-side Loans, which stay live while the values that carry them are used.

A `uniq` requirement creates an exclusive Loan, and an exclusive Loan stays exclusive while a result depends on it; Origin equalities neither create nor release Loans. Protection begins when a Borrow is formed, in evaluation order (eligible exclusive borrows start as reservations, [§15.6.7](#1567-call-reservations)), and lasts through the whole call, including callee cleanup. Which results and writable storage, including storage reached through a Closure's captures, may retain an argument is determined by the instantiated public contract, for direct and indirect calls alike; an unproven absence of retention never permits access. A destruction retains as a call of each `drop` it runs, whose `self` is a `uniq/Self` receiver referring to the destroyed part ([§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)), and a callee that destroys a by-value input or the elements of its receiver includes that cleanup in its own contract, as it does its effects ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)); for an operation of [§4.6](04-arrays-indexing-and-slices.md#46-positions-and-ranges) to [§4.9](04-arrays-indexing-and-slices.md#49-dynamic-collections), that cleanup is the destruction its row lists, composed at the call, never a library body's own ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)). Retention is decided per argument, never per Origin: a result or storage that may retain an Origin inside a borrowed argument's referent Type, including a Closure's call receiver, carries every Loan that a read through the argument carries ([§15.6](#156-borrow-checking)), whichever of those Origins it names; one that may retain any other Origin of an argument carries the argument's Loans where it can hold a value of its Type, of a part it holds by value, or a borrow formed through it, and those of such a read too when the argument is a borrow. A raw pointer argument is retained as a borrowed one, its pointee as the referent: a result or storage that may retain an Origin inside its pointee Type carries what a read through it carries. The argument's own value, its Loans and the holders it addresses, is retained only by a result or storage that can hold a value of its Type, of a part it holds by value, or a borrow formed through it ([§15.6.3](#1563-reborrowing-and-region-splitting)), by a result the call forms within the argument's storage ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places), [§5.6](05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations)), or by a raw layer without an Origin, below. Storage of an abstract Type can hold such a value only where the premises make it one, or where a callable the instantiated contract hands the callee's body (a Callable bound, a requirement of a Contract premise, or a callable value an input holds) may retain the argument there by this section applied to that callable's signature, through its result or its writes. A pointer is never part of its own pointee: storage a call writes through a raw argument never retains that argument's own value. A raw layer retains as [§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins) states: with an Origin, as the borrow that Origin belongs to would, without a Loan; without one, a parameter's raw layer ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)) admits what its pointee Type admits, and a raw layer that the call may write, in its result, a nominal Type's Fields included, or in storage it writes through an argument, may retain every argument with what any chain of reads through it carries, but not the own value (its Loans and the holders it addresses) of the argument through which the call writes it, and reaches storage the body does not own. A chain of reads goes through a raw or borrowed layer anywhere in the argument's value (a Tuple element, an Option payload, a Field or a Type argument included), then through one in the value read, and so on. A raw layer behind a borrow in the result, a Slice's or UniqSlice's elements included, is lent, not written: it retains only the raw arguments, with what any chain of reads through them carries, as a result the call forms within their storage does, and reaches no storage the body does not own. A published operation that transfers values is an Iterator's `next` ([§22.2.3](22-core-execution-and-foreign-functions.md#2223-iterator-independence)), `exchange` or `swap` ([§15.7](#157-whole-value-updates)), or an operation of [§4.6](04-arrays-indexing-and-slices.md#46-positions-and-ranges) to [§4.9](04-arrays-indexing-and-slices.md#49-dynamic-collections); at a raw layer without an Origin it retains a borrowed argument (not a raw pointer argument), a Slice or UniqSlice included, only by what a chain of reads through it carries, never by its own value; there, `exchange`'s result, the old contents, retains only what a chain of reads through its target carries. In the element storage it writes, an operation of [§4.9](04-arrays-indexing-and-slices.md#49-dynamic-collections) retains at a raw layer without an Origin, of its arguments other than the receiver, only its added and replacing inputs, and a removed element in its result keeps the stored Types' full dependencies ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)). A call writes through no raw layer that it reaches past a shared layer ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)).

A destruction whose `drop` stores through a raw layer retains as that call would:

```kimi
func usep(p: raw/u8) => ()

struct Writer
    public var p: raw/(raw/u8)
    public var q: raw/u8
    public init(q: raw/u8)
        self.p = null
        self.q = q
    drop
        unsafe => *self.p = self.q          // Retains as drop(self: uniq/Writer) would.

func reset()
    var y: i32 = 1
    let w = y@ref
    var slot: raw/u8 = null
    var x = Writer.init(q: w@raw@raw/u8)    // x.q addresses w, which carries y's Loan.
    x.p = slot@raw
    x = Writer.init(q: null)                // The old x's drop may store its q in slot.
    // y = 5                                // Error: slot may retain q, which addresses w, which carries y's Loan.
    usep(slot)
```

**Effect summaries.** A callable's **effect summary** lists the static Fields ([§22.4.4](22-core-execution-and-foreign-functions.md#2244-static-storage-keys)) whose storage it may access with its reads, borrows, writes, replacements, destructions and first-access initializations. It includes the effects of its callees, of every implementation in its closed explicit-specialization set ([§12.7.2](12-expressions.md#1272-implementation-families)), and of defaults, accessors, lazy initialization and cleanup. It is computed once per declaration, in terms of its own Type parameters, as the least fixed point over recursive calls. Its **parameter-dependent effects** are those it keeps in terms of a Type parameter or an associated-Type projection: destructions of such a Type not proven Copy ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), the user-code effects of published operations on such a Type ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)) and calls through Callable inputs. When a summary would keep one on a projection one of whose bases (its base, that base's base, and so on) the premises of the summary's declaration prove to conform to a recursive Contract ([§8.3.3](08-generics-constraints-and-contracts.md#833-recursion-and-finality)), it has unknown environment effects instead. The other parameter-dependent effects have a bounded projection depth, so the fixed point is finite. Calls through Callable inputs are recorded once per input and receiver mode and composed with the actual arguments at each use. Conformance effect bounds are checked against the completed summaries ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)).

- A call or direct static access that conflicts with a live Loan ([§15.6.2](#1562-place-overlap-and-conflicts)) is an error. A live Loan anchored to a Field proves only that Field initialized.
- A call with unknown environment effects, or an erased destruction ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), conflicts with every live Loan anchored in mutable static storage, shared or exclusive, regardless of the Field's visibility. Loans anchored in immutable static storage are never affected. A task call always has unknown environment effects ([§24.3](24-suspension-and-asynchronous-tasks.md#243-task-calls)).
- Requirement and Callable-constrained calls take their effects from effect bounds ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use), [§8.11.4](08-generics-constraints-and-contracts.md#8114-callable-effect-bounds)).

**Mutable-static Loans.** A Borrow of mutable static storage is bounded by the body ([§15.6.5](#1565-judging-origin-relations)), so no value that carries its Loan leaves the body, and every call takes its results' dependencies from the public contract alone.

```kimi
group State
    public var text: string = ""
    public var count: i32 = 0
    public let limit: i32 = 10

func current(anchor: ref/i32) -> ref/string during anchor
    return State.text@ref              // Error: a borrow of mutable static storage never leaves the body.

func leak(anchor: ref/i32, target: uniq/(ref/string during anchor))
    target@follow = State.text@ref     // Error: nor through a parameter.

func run(g: () -> ())
    let c = State.count@ref
    let l = State.limit@ref
    g()           // Error: g's unknown effects conflict with c, used below.
    inspect(c)
    inspect(l)    // l alone would not conflict.
```

A widened effect conflicts as an unknown call does, even where the caller's premises would classify every effect it replaces:

```kimi
contract Tree
    associate Child is Tree       // Recursive (§8.3.3).
    func child(self: ref/Self) -> Option<Self.Child>
        effect confined

contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree // Every U.Child… of a CopyTree U is Copy.

group Trees
    public func walk<T>(node: T, depth: i32)
        T is Tree
        if depth == 0 => return
        match node.child()
            .Some(let next) => Trees.walk(next@move, depth - 1)
            .None => return
// walk's summary has unknown environment effects: T.Child has the recursive base T.

group Counters
    public var count: i32 = 0

func visitCopies<U>(node: U)
    U is CopyTree
    let c = Counters.count@ref
    Trees.walk(node, 2)           // Error: walk's environment effects are unknown and conflict with c.
    inspect(c)
```

### 15.6.5. Judging Origin relations

A body must work for every caller of its universally quantified signature. An Origin relation is judged Proven, Refuted or Unknown ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)). Origins are of three kinds:

- A **fixed Origin** is fixed by the body's contract: a universal Origin, a parameter or receiver Origin, a projection of one, or `static`. A fixed Origin contains every point of the body.
- A **finite Origin** belongs to a Borrow with an upper bound; each such Borrow has its own.
  - A **body-local Origin** belongs to a Borrow anchored in a local, parameter or temporary root of the body, or in the receiver of a constructor or `drop` ([§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver), [§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)), reached by Field, element or owning-layer projections, never through a borrow or raw pointer; its bound is the body. Implicit Borrows and capture-entry Borrows ([§7.7.2](07-functions-and-callable-values.md#772-captures)) are included. A Closure's environment bindings are Fields of its call receiver's referent ([§15.8.2](#1582-closure-dependencies)), so borrowing them through a borrowed receiver is not body-local; in a Consuming call it is.
  - A Borrow of mutable static storage, including an inline part, is bounded by the body too: a relation that carries it to a fixed Origin, such as returning it or storing it through a parameter or a capture, is Refuted ([§15.6.4](#1564-calls)).
- An **inferred region** is any other local Origin, such as a slot of an unannotated local's Type. A Borrow of a raw Place ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)) has no upper bound of its own (through a pointer that states an Origin it is contained in that Origin, [§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)), so its Origin is not finite; it is an inferred region. So is the Origin of a Borrow reached through a borrow: it has no bound of its own, and its containment in the borrows it is reached through follows [§15.6.3](#1563-reborrowing-and-region-splitting).

Every relation the body checks is judged, including unreachable result sources ([§14.9](14-control-flow.md#149-result-validation)) and the assumed-path state of [§14.10.3](14-control-flow.md#14103-unreachable-code); judging uses neither liveness nor reachability. Each relation `a outlives b` is an edge from `a` to `b`, and `a == b` is edges both ways. A **chain** is a path whose ends are fixed or finite Origins and whose inner nodes are inferred regions or finite Origins; it requires its first end to outlive its last. Chains are judged by reachability, not by enumerating paths:

- **Refuted:** a chain requiring a finite Origin to outlive a fixed Origin outside its bound. This is the only rule that refutes an Origin relation.
- **Proven:** a chain between fixed Origins that the solver ([§15.3.6](#1536-origin-inference)) derives from the premises; or a chain with a finite Origin or inferred region at one end that is not Refuted, which then constrains region inference.
- **Unknown:** a chain between fixed Origins that the solver cannot derive.

With pair binders, the well-formedness premises are those of each Semantics case's Types ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)); a pair layer's outer-Origin slot exists only in cases that have an outer Origin ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)), and a relation is Proven only from premises that hold in every case that requires it. A use beyond a local Place's lifetime is a Loan conflict at its destruction or Move ([§15.6.6](#1566-destruction-lifetime-checking)), never an Origin relation failure; returning a borrow of a local or temporary is the Refuted relation at the returned value.

A Loan conflict on the Loan of a Borrow whose finite Origin Refuted chains carry to fixed Origins is caused only by the Refuted relations of those chains ([§23.1.2](23-compiler-services.md#2312-reporting)) when every value that carries the Loan and may be used after the access is the result or storage the body does not own and received the Loan only as the borrow or a Copy or Reborrow of it ([§15.6](#156-borrow-checking)), stored into a part with a fixed Origin; a call's result, or storage the call writes, that may retain such a value as an argument counts as a Copy of it ([§15.6.4](#1564-calls)), and a read through a reference to a whole value counts as a Copy of that value.

```kimi
func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    return local@ref      // Error: the borrow of local cannot outlive x.

func f(x: ref/i32, y: ref/i32) -> ref/i32 during y
    let r = x
    return r              // Error: the chain x -> r -> y needs x outlives y, which is not proven.

// Box<E> has the Field item: E.
func pick<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c
    s is owner or ref
    return x              // Error: a outlives c holds only in the ref case.
```

```kimi
func store(target: uniq/(ref/i32 during a))
    var local: i32 = 1
    target@follow = local@ref   // Error: the borrow of local cannot outlive a.
    local = 2                   // Error: caused only by that relation, since only target.* keeps the Loan.

func keep(x: ref/i32) -> ref/i32 during x
    var value: i32 = 1
    let r = value@ref
    value = 2                   // Error: independent of the relation below, since r keeps the Loan and is used after.
    return r                    // Error: the borrow of value cannot outlive x.
```

### 15.6.6. Destruction lifetime checking

Destruction responsibility and order follow [§16.2](16-scope-exit-and-destruction.md#162-scope-exit) and [§16.3](16-scope-exit-and-destruction.md#163-destruction-declarations). Every Origin and Loan that destruction may observe must be valid there. A user-defined `drop` is assumed to observe all reachable Origins, even if its body uses none, and the check applies recursively to Field destruction; destruction that observes no Origin adds no requirement.

A destruction that may observe a Loan is an exclusive use of the destroyed Place and of the Places reached from it, across `obj`, `rc` and `arc` payloads and exclusive dereferences up to the first shared dereference ([§15.6.2](#1562-place-overlap-and-conflicts)), at `=`, at the end of storage and in a call that destroys a target, such as `replace`; it is checked before it ends any Loan ([§15.6.3](#1563-reborrowing-and-region-splitting)). A destruction that observes nothing accesses only the destroyed Place.

```kimi
// Log is a group with observe(value: ref/Writer).
struct Logger {sink}
    let out: uniq/Writer during sink
    drop
        Log.observe(self.out)    // sink must be valid here, even for an empty drop.
```

```kimi
// S has the Field r: uniq/i32 and a drop that writes through it.
var x = 1
var y = 2
var s = S(r: x@uniq)
let c = s.r@follow@uniq          // A child Loan: path s.r.*.
s = S(r: y@uniq)                 // Error: the old s's drop may write through s.r while c is live.
c@follow = 1
```

### 15.6.7. Call reservations

A **call reservation** delays exclusive access, not evaluation or lifetime protection. An exclusive Borrow or Reborrow that produces a receiver or argument of an invocation starts a reservation instead of a live Loan. This covers the written forms `@uniq`, `@uniq/T`, `@follow@uniq` and `@objuniq`, and the implicit ones: the exclusive acquisition of a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-receivers)), the exclusive Reborrow of a borrow value at a `uniq` or `objuniq` parameter ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), permitted exclusive payload follows, and generic adaptations whose resolved effect is exclusive. The path from the root through standard Field and element projections that call no user code, up to that receiver or argument, is one preparation and ends at the lending point ([§3.4](03-types-and-values.md#34-places-and-storage)), even when it contains written exclusive borrows; no exclusive Loan on that path is live before the invocation. So `holder@uniq.items.append(x)`, `holder.items@uniq.append(x)` and `holder.items.append(x)` are the same call.

- Parentheses are transparent. A getter or another invocation on the path activates at its own call. Every invocation kind follows the rule. Overload selection comes first; reservation legality never changes it.
- An assignment target starts no call reservation; compound assignment, increment and decrement protect their destination as [§13.7.2](13-operators-and-assignment.md#1372-compound-assignment) states.
- No reservation passes through local or aggregate storage, captures, nested invocations, results of control-flow expressions, compound assignment, increment and decrement, or index expressions. A Reborrow saved in storage, including the bare acquisition of a stored exclusive reference, is live when formed.

**Reservation and activation.** The target is evaluated, located and checked once, and its Place, owner and the references it was reached through are protected: shared reads and Borrows may coexist; any write, Move, destruction, reallocation or other exclusive operation on it is an error. A child reservation keeps its parent's authority. The receiver is located first, then explicit arguments are evaluated in textual order and defaults in parameter order ([§7.2](07-functions-and-callable-values.md#72-parameters)); defaults may inspect reserved inputs through shared Loans that end before entry. An activation does not conflict with its reservation's own Loan that a recursive default's value carries ([§7.2.3](07-functions-and-callable-values.md#723-default-evaluation)). Then every reservation must be able to become live at once, with retained dependencies, captures, effects on static storage and cleanup effects; overlapping arguments that are both exclusive, or one shared and one exclusive, are errors even if the callee would not use them. Overlap between arguments is judged through the Loans their values carry, against which each activation is checked ([§15.6](#156-borrow-checking)): a Borrow past a shared dereference forms no Loan of its own ([§15.6.3](#1563-reborrowing-and-region-splitting)), so `g(s@uniq, s.r@follow@ref)` with `r: ref/i32` is valid. No temporary is destroyed early to make activation possible. A control transfer keeps reservation protection while its operand is evaluated and acquired; then only the reservations of the invocations it abandons end, without activation, before the abandoned preparation's remaining cleanup. A transfer caught inside an argument abandons nothing.

```kimi
// tasks: Array<Task>.
var p: i32 = 1
Kimi.Intrinsics.replace(p@uniq, with: p + 1)  // Valid: p is read during the reservation.
tasks.insert(tasks.length, Task.init(9))      // Valid: a shared read during the reservation.
// tasks.append(tasks.remove(0))             // Error: two exclusive acquisitions of tasks overlap.

struct Inventory
    var newest: Item
    func newestItem(self: uniq/Self) -> ref/Item during self => self.newest

var inventory = makeInventory()
let item = inventory.newestItem()             // inventory stays exclusively borrowed while item is used.
// inventory.newest = makeItem()              // Error.
inspect(item)
```

## 15.7. Whole-value updates

These `Kimi.Intrinsics` declarations ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)) are compiler-intrinsic and impose no `Sealed`, `Copy` or `Owned` Constraint.

```kimi
group Intrinsics    // Kimi.Intrinsics
    public func replace<T>(target: uniq/T ! with => value: T) -> ()
    public func exchange<T>(target: uniq/T ! with => value: T) -> T
    public func swap<T>(first: uniq/T, second: uniq/T) -> ()
```

| Operation | Old contents | New contents | Result |
| --- | --- | --- | --- |
| `replace` | Destroyed in place | Installed after destruction completes | Unit |
| `exchange` | Transferred | The acquired value | The old value |
| `swap` | Both transferred | Exchanged | Unit |

- Each target must be fully Initialized, exclusively writable and permitted a whole-value update. `T` is fixed by the targets (`uniq/T` is invariant in `T`), and `value` is fitted to `T` as an ordinary argument ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)).
- Each target is the complete storage of a `T`: ordinary owner storage may contain an open Core, while object payload storage needs the complete-target proof of [§13.5.5](13-operators-and-assignment.md#1355-follow); a base subobject or an open object View never qualifies.
- Targets are arguments: an owned Place is lent with `@uniq`, a borrow value is Reborrowed without a spelling ([§15.1.5](#1515-access-operations-and-acquisition)), and an object payload is selected with `handle@follow@uniq`. For a borrow Type `T`, the reference moves, not its referent.
- They cannot repair Uninitialized or partially Moved storage or Move out through a borrow. Replacing a whole value with `let` Fields permits no write to those Fields.

### 15.7.1. Evaluation and transfer

Argument evaluation, reservation and activation follow [§15.6.7](#1567-call-reservations); acquisition and fitting finish before any update, and an argument that does not complete leaves the target unchanged. `replace` destroys the old value in place in the normal `drop`, Field and base order; an Abort or divergence there prevents placement. Placement runs no constructor, initializer or setter. `exchange` and `swap` run no user code, destruction or Aborting operation.

```kimi
var p: i32 = 0
Kimi.Intrinsics.replace(p@uniq, with: p + 1)
let old = Kimi.Intrinsics.exchange(p@uniq, with: 5)
var q: i32 = 9
Kimi.Intrinsics.swap(p@uniq, q@uniq)
// Kimi.Intrinsics.swap(p@uniq, p@uniq)            // Error: overlapping targets.
// Kimi.Intrinsics.exchange(x@uniq, with: x@move)  // Error: transfers reserved storage; write x = x@move.
```

### 15.7.2. Static non-overlap

`swap` requires its targets to be proven disjoint by [§15.6.2](#1562-place-overlap-and-conflicts); live exclusive inputs `a: uniq/T, b: uniq/T` qualify, while runtime comparisons do not.

### 15.7.3. Storage update dependencies

These rules apply to every whole-value update of a Place: `=`, compound assignment, increment and decrement, `replace`, `exchange` and `swap`. The lifetime of storage is distinct from the lifetime of its contents ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)), and raw pointers cannot substitute for Loan evidence.

- Every Loan and Origin needed by incoming, returned or surviving values, and the operation's exclusive Loan and parent chain, must be kept.
- An update that would invalidate live borrows anchored in the old contents is an error; placing a new value at the same address never restores them. Independent external dependencies survive: a Copy of a stored `ref/Data` keeps only its external Loan, whereas a borrow of the Field depends on the Field.
- Updating an object payload keeps the object's Identity, allocation, Dynamic Type and ownership mode; it is not final release. An empty or destroying payload is never exposed. An exchanged old value carries its own destruction obligation.
- Facts about the old contents are invalidated; object Identity and Dynamic Type refinements are kept ([§14.10.1](14-control-flow.md#14101-refinable-bindings-and-effective-types)). Handle replacement follows [§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release).

```kimi
var data = makeData()
var other = makeData()
var slot: ref/Data = data@ref
let copy = slot                 // Depends on data only.
let inner = slot@ref            // Borrows the slot itself.
slot = other@ref                // Error: inner is used below.
inspect(inner)
```

## 15.8. Captured and erased dependencies

### 15.8.1. Object payload erasure

Erasing a concrete payload behind a base or runtime-Contract view requires its complete data Type to be `Owned` ([§15.2.3](#1523-static-and-owned)); the handle's own Origin is not part of the test, so a local object borrow keeps a local Origin. Creating an object requires [§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload). An erased view certifies the proof; later upcasts and casts inherit it, as does lexical `self` in an override body ([§6.4.5](06-declarations-and-containers.md#645-receiver-safety)). Only proof-covered fixed bindings may be supplied as `static` in the Target completion of runtime tests and casts ([§13.6.5](13-operators-and-assignment.md#1365-checked-casts-and-target-completion)). The proof lasts for the object's lifetime because `obj` and `objuniq` are invariant in their View Target and other handles grant no mutation.

### 15.8.2. Closure dependencies

A Closure keeps every captured value's Origin and Loan dependencies, not merely the lifetime of its creation Block. Moving in an owned value without borrowed contents borrows nothing, and an entry that borrows an outer slot ([§7.7.2](07-functions-and-callable-values.md#772-captures)) keeps its Loan on that slot.

A Closure body is checked against its call receiver ([§7.7.3](07-functions-and-callable-values.md#773-call-receiver)) as an instance function's body is against its receiver: its environment bindings are Fields of the receiver's referent, so its result Origins, containment ([§15.6.3](#1563-reborrowing-and-region-splitting)) and retention ([§15.6.4](#1564-calls)) follow the receiver rules. Each environment binding's permissions stay those of [§7.7.2](07-functions-and-callable-values.md#772-captures), and retention into the environment follows them, since no Closure body can replace its environment: an Exclusive call may retain an argument in its `var` bindings and in the referents of its captured exclusive references. The table restates the receiver rules for each source of a result:

| Result source | Contract |
| --- | --- |
| Borrow of environment-owned data | Depends on the call receiver borrow |
| Captured shared reference, or a shared Borrow through it | Keeps its external Origin |
| Reborrow of a captured exclusive reference | Keeps its external Origin and Loans and, in a Shared or Exclusive call, depends on the call receiver ([§15.6.3](#1563-reborrowing-and-region-splitting)) |
| Reference moved out by a Consuming call | Transfers its Origin and capability |
| Borrowed argument | Follows the parameter's Origin contract |

When the result Type is omitted, each Origin in the inferred result is a per-call Origin of the internal call contract, outlived by every source of the returned values by the receiver rules. Several returned values give the meet of their sources at covariant positions and require equal Origins at invariant positions. The Origin of a whole result inferred from the body is a fixed Origin outside the body; each result source is judged against it, so a source whose Origin is body-local, such as a Borrow of call-local storage or of an environment consumed by the call, is Refuted at the returned value, not a Loan conflict at the storage's destruction ([§15.6.5](#1565-judging-origin-relations)), while a source of Origin `static`, such as a Borrow of an immutable static Field, outlives it.

An annotated result, or a fixed expected call signature ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)), uses ordinary result elision ([§15.4.3](#1543-result-defaults)) without the hidden receiver, which no Origin expression names; annotation names resolve among the parameters and enclosing scopes, never to environment bindings. The receiver stays hidden because common Function Types and Callable Constraints never return results that depend on it ([§3.2.1](03-types-and-values.md#321-callable-value-types), [§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)).

```kimi
let text = makeText()
let get = func [text@move] () => text@ref   // Result depends on the call receiver.
let view = get()
// let moved = get@move                     // Error: view is used below.
inspectText(view)

let other = makeText()
let invalid = func [other@move] () -> ref/string => other@ref
// Error: the annotated result defaults to static, not the environment borrow.
```

Retention into the environment follows the binding's permissions:

```kimi
func update(z: uniq/i32, x: ref/i32, out: uniq/i32)
    origin z outlives x
    let first: ref/i32 during x = x
    var copyOut = func [first, out] (value: ref/i32 during x) => out@follow = value@follow + first@follow
    copyOut(z@follow@ref)   // An Exclusive call; first is a let binding and out's referent an i32, so neither keeps value.
    z@follow = 42           // Valid.
    copyOut(x)

    var last: ref/i32 during x = x
    var remember = func [var last] (value: ref/i32 during x) => last = value
    remember(z@follow@ref)  // last may keep value.
    // z@follow = 43        // Error: remember is used below.
    remember(x)
```

A bare entry naming an enclosing local Reborrows it with that local's Origin ([§7.7.2](07-functions-and-callable-values.md#772-captures)):

```kimi
var x: i32 = 1
let u = x@uniq
let t = makeText()
let take = func [u, t@move] () -> uniq/i32 during u
    consumeText(t@move)     // A Consuming call.
    return u                // Valid: the binding has u's Origin, and a Reborrow through it may be returned (§15.6.3).
let a = take@move()
a@follow = 3
```

### 15.8.3. Escape and retention

Returned values, stored Fields, other Closures and indirect callees are checked against the destination's lifetime and capability contracts. Borrow capture is not inherently non-escaping, Move capture keeps nested borrow dependencies, and moving or heap-allocating a Closure never extends a referent's lifetime. Neither `ref/F` nor Callable promises that an environment-derived value is not saved. `Owned` grants no thread transfer ([§D.5](appendices/D-deferred-features.md#d5-concurrency)).
