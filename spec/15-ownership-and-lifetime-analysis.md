# 15. Ownership and lifetime analysis

[Specification index](../SPEC.md)

## 15.1. Initialization and consume analysis

This section checks the initialization, completeness and Consume legality of the Places defined in [values, places and storage](03-types-and-values.md#34-values-places-and-storage).

### 15.1.1. Storage, state, and responsibility

Initialization state and destruction responsibility are tracked per Place:

| State | Meaning | Read / borrow / Copy / Move | Write to `let` | Write to `var` |
| --- | --- | --- | --- | --- |
| Uninitialized | No initialized value is held | Forbidden | Only if never initialized | Initialization |
| Initialized | An initialized value is held | Subject to Type and access rules | Forbidden | Replacement |
| Moved | The former value or capability and its responsibility were transferred | Forbidden | Forbidden | Reinitialization |

Moved records the source's history, not whether the transferred value is still alive. The first initialization of a `let` Place is tracked separately; neither a Move nor internal destruction resets it, and no user operation destroys a Place to reset it. State alone grants no access or write permission.

Every read, borrow, Copy or Move requires the Place to be Initialized on every incoming Runtime Reachability path (§14.9.2). Unreachable code uses the separate checking state of §14.10.3; the absence of an execution path does not make an unusable value usable. A `let` permits one initialization per binding lifetime on each path; a `var` permits later initialization and replacement under ordinary permissions. Fields also obey their own access rules.

```kimi
var number: i32
if condition
    number = 1
else
    number = 2
print(number)             // Both paths initialize number.

let resource: Resource
resource = makeResource()
consume(resource@move)    // Move.
resource = makeResource() // Error: let cannot be initialized again.
```

### 15.1.2. Aggregate construction and completeness

Two facts are tracked independently: **construction completion** records that construction finished successfully, and **current completeness** requires every stored component to be Initialized and complete. A **complete value** has both. The components of a derived struct are its base subobject and its own Fields, and completion is tracked separately for each layer. The components of an enum are only the active Case's payloads; [Case construction](06-declarations-and-containers.md#632-case-construction-and-resolution) commits its completion, and inactive Cases need no initialization. No stored field is optional to initialize. Computed Properties are not components and need no storage initialization.

Only the [constructor phases](06-declarations-and-containers.md#623-constructors) commit completion: success is validated, cleanup finishes with the storage alive, and completion is committed before the value is exposed or transferred. No user operation commits completion early or resets it, and an incomplete exit or an Abort never commits. Initializing every field does not skip the remaining constructor work. A complete field or base keeps its own completion and destruction responsibility even if its enclosing layer never completes.

While a value is incomplete, whole-value reads, Copy, borrowing, Move and exposure are forbidden, including through ordinary accessors that take the whole `self`; direct operations on initialized fields follow their own access rules. After a Partial Move of a value whose construction completed, direct [Field operations](11-properties.md#1112-move-paths-and-inherited-fields) are allowed; this does not authorize access during initial construction.

A Partial Move changes current completeness, not construction completion. Permitted reinitialization of all missing fields restores completeness without rerunning a constructor. If a field cannot be reinitialized, the value stays incomplete and cannot be used or transferred as a whole, although its remaining Initialized parts can still be used and cleaned up; there is no separate permanent-incomplete state. A whole-value Move transfers the construction-completion fact, and a whole replacement uses the new value's fact.

Tuple and array construction places elements in increasing index order. Each element acquires its own initialization and responsibility only when its placement completes normally, and a partly built element is tracked recursively. The aggregate commits completion after all elements are placed. If an element expression leaves the construction by an ordinary control transfer, that transfer's result is secured first, and then the abandoned construction's remaining elements are cleaned up in decreasing index order. Previously moved arguments and completed side effects are not rolled back. Raw uninitialized memory is not an alternative safe construction syntax. Fixed arrays additionally require [whole initial construction](04-arrays-indexing-and-slices.md#43-initialization-and-inference); static-path repair after completed construction remains permitted.

### 15.1.3. Move paths and partial move

A **Move Path** is a statically trackable path with its own initialization state and destruction responsibility. Move Paths are formed by Fields reached through statically known base-subobject paths, Tuple elements, fixed-array elements at constant indices (below), and combinations of these. Runtime indices, dynamic containers and user indexers never form Move Paths, even with literal indices, and neither constant propagation by an optimizer nor integer proofs add any.

**Constant fixed-array indices.** A ConstantIndexExpression is one nonnegative integer literal token, optionally enclosed in any number of grouping parentheses. Every integer base and digit separator of §2.6 is accepted; the value is decoded by the lexical integer rules and must fit `isize`. Recognition involves no arithmetic, conversion, name lookup, general constant evaluation or target-dependent evaluation.

~~~ebnf
ConstantIndexExpression := IntegerLiteral | "(" ConstantIndexExpression ")"
~~~

For a resolved fixed-array Type `[N of T]`, such an index forms an element Move Path only when its value `n` satisfies `0 <= n < N`. Path identity uses the value, so `1`, `0x1` and `(1)` designate the same element. Overlap analysis uses the same rule (§15.6.2). The rule grants no path through a dynamic collection and no permission to Move through a borrow.

| Index expression | Static fixed-array Move Path |
| --- | --- |
| `0`, `(0)`, `((0))`, `0x1` | Yes, when it fits `isize` and is in bounds |
| `1 + 1`, `-1`, `+1`, `3@isize` | No; operators and conversions are outside this grammar |
| `if condition => 1 else => 2`, an immutable Name, `^1` | No; selection, name propagation and from-end resolution are not literal recognition |

An out-of-range literal forms no element path, but that alone does not make an otherwise valid index operation a compile-time error: its bounds check follows §4.6 and §17.3.4 and Aborts if executed. An operation that requires a Move Path is rejected when none exists, whether or not its bounds check could fail. A literal that does not fit `isize` is an ordinary compile-time error. Thus `array[1 + 1]` is usable for ordinary permitted reads but never gains Partial Move eligibility or a disjointness proof.

Within an owned match Subject, selected Case payload positions also form Move Paths ([match acquisition](#1516-match-acquisition-and-lifetime)); they grant neither general payload access nor a Partial Move from the caller's enum.

A Move Path defines tracking granularity, not access permission:

| Source access | Partial Move |
| --- | --- |
| Tuple element / constant-index fixed-array element | `@move` transfers the element; a bare read Copies a Copy value |
| Stored Property slot of Type `F` | `@move` transfers the value under the permissions of §11.1; a standard bare read Copies it when `F` is Copy |

```kimi
var pair: (string, i32) = ("Alice", 30)
let name = pair.0@move  // Partial Move; pair is incomplete.
let age = pair.1   // Remaining initialized part is usable.
// let all = pair  // Error: incomplete.
pair.0 = "Bob"
let all = pair     // Complete again.
```

A Non-Copy referent reached through `ref`, `uniq`, `objref` or `objuniq`, or any part of it, is never Moved in a way that leaves the borrowed Place Moved or Uninitialized, even if reinitialization is planned: exclusive access does not transfer ownership. A Copy leaves its source Initialized and is not extraction.

**Partial Move and destructors.** A Move leaves its path Moved, and initialization restores it. Completeness is required immediately before every whole-value use (§15.1.2), including a call that receives the whole value, and before a user-defined `deinit` runs. An ancestor's `deinit` does not by itself forbid a Partial Move. Instead, the same state analysis must prove that completeness is restored on every path that reaches such a point, including normal completion, `return`, `try` propagation, `exit`, `continue`, `yield`, `defer` bodies and the cleanup of abandoned construction. Planned assignments and optimization results are not proof.

```kimi
// Box.value is a mutable Resource; Box has a deinit that needs a complete Box.
var box = Box.init(Resource.init())
let saved = box.value@move  // Partial Move.
// inspect(box)             // Error: a whole-value shared borrow of an incomplete Box.
box.value = Resource.init() // Direct initialization; the whole is not borrowed.
inspect(box)                // Complete again.
```

If the restoring value is obtained with `try`, the failure return must also be able to destroy `box`. Abort does not unwind and needs no destructor on the paths it cuts off, but a failure return is an ordinary exit, not an exception. The cleanup at a destruction point follows §16.3.2. Restoration through `defer` or an inner destructor is checked in execution order; nothing proceeds past a cleanup that does not complete normally, and nothing is destroyed twice.

When ordinary extraction is unavailable, use [exchange or swap](#157-whole-value-updates) or a Type-specific operation. Exchange preserves initialization; a Type-specific operation keeps its invariants itself and may need restricted storage access. A Field path never bypasses an intervening computed getter.

### 15.1.4. Consume verification and representation

Consume is verified by two separate static checks; they are not runtime fallback stages:

```text
Consume
├─ Eligibility: does the declaration, Type, and path provide the operation?
│  ├─ supported place kind and ownership path
│  ├─ trackable Move Path
│  ├─ required Field declaration and storage properties
│  └─ structural Partial Move / deinit restrictions
└─ Legality: may this use site perform it?
   ├─ required accessibility
   ├─ target Initialized on every incoming path; complete if an aggregate
   ├─ required receiver construction previously completed
   ├─ no conflicting Loan; valid Origins
   └─ actual ancestor path and Destruction conditions
```

The declaration kind is structural, while accessibility depends on the use site. Constraints can prove structural facts, but not current initialization or the absence of Loans. Unknown structural facts follow [generic Access Effect resolution](08-generics-constraints-and-contracts.md#89-generic-access-effects); there is no Consume contract syntax.

An ancestor of the target may be incomplete if the target itself is Initialized and complete and can be located without whole-value access to that ancestor. A user-defined `deinit` can make a path structurally ineligible, so the actual ancestors are also checked at each use. Moving a complete value as a whole is not a Partial Move.

Per-path state, destruction responsibility, the first initialization of a `let`, construction completion and current completeness are tracked across branches, loops, transfers and `defer`, and [destruction lifetime checks](#1566-destruction-lifetime-checking) apply. Raw-pointer operations need not recover or repair the responsibility of an untracked original owner.

Lowering may elide transfers and temporary storage, or use conditional cleanup flags, only while preserving values, abstract Place identity and lifetime, Move state, Loans, Origins, destruction responsibility and specified failures. Optimization never changes which programs or Move Paths are legal.

### 15.1.5. Movable places

A **Movable Place** offers Take (§3.4) and meets the Move conditions below, so that its current value, with its ownership or capability, can be transferred out. Take is offered by owned root Places, permitted inline Fields and Tuple elements, and fixed-array elements at constant indices (§15.1.3). Borrowed referents, object payloads, static storage, elements of dynamic collections, Places published by functions (§7.1.1), other indices and hidden Property storage never offer Take; `remove` and owning iterators extract elements under contracts that update the collection's state and responsibility.

| Operation | Required conditions |
| --- | --- |
| Copy | Read; complete; the complete Type is Copy |
| Shared borrow | Read; complete; a valid Place and dependencies |
| Exclusive borrow | Read and Write; complete; exclusive capability of the lending point |
| Move | Take; complete; a trackable owned path |
| Initialize | Uninitialized; the initialization permission of the declaration and current state |
| Replace | Write; the old value can be destroyed legally; no dependency of the old or new value is broken |

Every operation also checks access, Origins and Loan conflicts. The first initialization of a `let` and initialization during construction are dedicated permissions, not derived from Write or Take. A Moved `let` cannot be reinitialized, and Uninitialized storage cannot be borrowed as a safe `uniq/T`. Generic owned arguments may be Moved as whole values without an extra Contract. Raw dereference keeps its Unsafe obligations. An unknown `T` is acquired as `T`; no result Type depends on Copy capability.

**Lending rule.** A transfer from a Place is written `@move`; an owned temporary is transferred without a spelling. A new exclusive borrow is written `@uniq` or `@objuniq`, except for a Receiver Expression, which is acquired implicitly under [§7.3](07-functions-and-callable-values.md#73-explicit-receivers) whatever its value kind ([§3.4](03-types-and-values.md#34-values-places-and-storage)). A borrow value needs no spelling: it is Reborrowed in the mode that a fixed expected Type requires, never stronger than its own mode or its path's authority; `@ref` or `@uniq` written on it borrows its slot instead (§13.5.5.2). Bare acquisition Copies a proven-Copy Type and never Moves (§3.5). `@move` requires a Movable Place and transfers even a Copy value: it marks the source Moved and transfers the complete Type, dependencies and destruction responsibility. Transferring a reference transfers its capability, not ownership of its referent. A `let` binding may be the source of a transfer; restoring a Moved `var` needs write permission.

At every position other than a Receiver Expression, including arguments, annotated initializers, assignment sources, results, defaults, and element and payload positions, each value kind is acquired by the [common adaptation](10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) when the expected Type is fixed, and by bare acquisition otherwise:

| Value kind | Acquisition at other positions |
| --- | --- |
| Borrow value | Reborrow, or one shared reference through its layers at an expected `ref/U` (§10.2), or a Scalar read at a Scalar; without an expected Type, Copy only for `ref`/`objref` |
| Owned Place | Shared borrow without a spelling at an expected shared borrow Type; exclusive borrow requires `@uniq`/`@objuniq` whatever the access path; by value, Copy when Copy and otherwise `@move` |
| Owned temporary | By value, passed as is; shared borrow materializes it; exclusive borrow requires `@uniq` |

**Exclusive acquisition conditions.** Exclusive acquisition of an owned Place or owned temporary, explicit or implicit, requires its lending point to be exclusively writable:

- a root that is a `var` local, mutable static storage (§15.2.3) or an owned temporary is writable;
- a standard projection inherits its root's permission and adds the Property permissions of §11.1 and the Loan restrictions (for example `values[i].update()`, §4.6);
- storage rooted in a `let` binding or an owned-Type parameter, and the storage of an owned getter result and its inline parts (§11.2.3), cannot be exclusively acquired.

The condition applies to the storage being borrowed, not to the expression it came from: a separate referent reached through a returned reference or handle has its own permission. A borrow value follows its own mode, so a `uniq` value held in a `let` binding or a parameter can still be Reborrowed exclusively.

```kimi
let number: i32 = 10
let copied = number       // Copy; number remains initialized.
let resource = makeResource()
let taken = resource@move // Transfer; resource is now Moved.
// let bad = resource     // Error: a bare Non-Copy Place never Moves.
```

```kimi
// describe takes ref/Array<Task>, consume takes Array<Task>, modify takes uniq/Resource.
public func main()
    var tasks: Array<Task> = []
    tasks.reserve(additional: 4)                     // Receiver: implicit exclusive acquisition.
    tasks.append(Task.init(1))
    let last = tasks.remove(^1)
    describe(tasks)                                  // Argument: shared borrow.
    var first = makeResource()
    var second = makeResource()
    Kimi.Intrinsics.swap(first@uniq, second@uniq)    // Exclusive argument borrows need a spelling.
    var r = first@uniq                               // r is a borrow value.
    modify(r)                                        // Reborrow: no spelling.
    modify(getExclusive())                           // A returned uniq/Resource is a borrow value too.
    consume(tasks@move)                              // A transfer from a Place needs a spelling.

func touch(target: uniq/Resource)
    target.update()                                  // Reborrow of a uniq parameter.
```

Getter results are acquired as results, never by moving hidden storage. An owned temporary transfers to its destination under §3.6; borrowing the destination does not restore the original source.

### 15.1.6. Match acquisition and lifetime

`match E` and `for` evaluate their Subject expression `E` once and initialize an internal **Subject Place** under the **subject rule**: the Subject is acquired as written, by the ordinary acquisition of `E` (§3.5, §13.5), except that a bare Place, which bare acquisition would Copy or reject, is borrowed in place. Parentheses change nothing, and operations inside `E` are ordinary expressions:

| Subject `E` | Subject Place |
| --- | --- |
| A bare Place | A shared borrow of the Place; when the Place stores `ref/T` or `uniq/T`, a Reborrow of that reference in its own mode |
| Any other expression: an explicit borrow such as `E@ref`, `E@uniq`, `E@objuniq` or a typed slot borrow, `E@move`, a temporary, or an acquisition such as `E@owner` | The value this acquisition produces: `@move` transfers even a Copy value, a temporary is transferred as is, and a same-Type acquisition Copies a Copy Place, which stays usable |

The **Subject mode** is the access that the Subject Place grants to its first layer that is not a safe reference:

| Subject Place | Mode |
| --- | --- |
| A shared borrow, or a `ref` or `objref` value | Shared |
| An exclusive borrow, or a `uniq` or `objuniq` value | Exclusive |
| An owned value | ByValue |

The mode never depends on `var`, Copy capability, an available conformance or the body. An explicit borrow applies to the immediately written slot (§13.5.5.2), so `values@uniq` on `let values: uniq/Array<T>` is an error; the referent is enumerated exclusively with the bare `values` or with `values@deref@uniq`. A shared layer anywhere on the path bounds the mode to Shared. `E@move` on a reference transfers the reference value, never ownership of the referent, so the Subject keeps that reference's mode. `for` selects its iteration entry from the mode (§14.6.2). `match` does not itself dereference the Subject; structural Patterns select the Places they need (§14.8.1).

The Subject Place is initialized before arm selection, whatever the bindings and Wildcards and whether or not any arm succeeds. A Shared Subject of a proven-Copy Place may be implemented by a Copy of its value, because its bindings are shared views of a value that the Loan rules keep unchanged while they are live. A Subject that accesses a Property with a custom, computed or required `get` invokes that getter once; a standard stored `get` uses the permitted Place operation. A borrowed Subject's Loan follows the uses of its bindings: an arm without a live borrowed binding may assign to or transfer the original Place.

```kimi
// Message is Non-Copy.
match message                       // Shared borrow.
    .Write(let text) => inspect(text) // text: ref/string
    _ => ()
use(message)                        // Valid after required Loans end.

match state                         // Arms without borrowed bindings may replace the Place.
    .Idle => state = .Running
    _ => ()

match message@move                  // Owned Subject: payloads are transferred.
    .Write(let text) => store(text@move)
    _ => ()
// use(message)                     // Error: the whole value was transferred.

match makeMessage()                 // A temporary is an owned Subject without a spelling.
    .Write(let text) => store(text@move)
    _ => ()

match count@owner                   // count: i32. A Copy acquisition: ByValue, count stays usable.
    var n => n += 1                 // n: i32

match counter@uniq                  // Exclusive Subject.
    .Count(let n) => n@deref += 1   // n: uniq/i32
    _ => ()
```

**Bindings.** Patterns select Places; once an arm is selected, its body locals are initialized left to right from the selected Places. An unguarded arm is selected as soon as its Pattern succeeds; a guarded arm also requires a true guard whose cleanup completed (§14.8.3). `let` and `var` decide only whether the new local may be reassigned; `var` grants no access to the original Place. On a shared or exclusive path, a `var` binding is therefore a reassignable reference. Assigning a value of its referent Type to it is an error whose diagnostic names the Subject mode and suggests `@deref` for an exclusive referent, or a ByValue Subject such as `E@owner` or `E@move` for a local value. Binding the whole of a borrowed Subject binds a reference to the original Place, never to the internal reference slot; for a Subject that Reborrows a stored `ref/E` or `uniq/E`, that Place is the referent, bound as `ref/E` or `uniq/E`. Binding the whole of an owned Subject transfers it.

| Selected Place | Value bound |
| --- | --- |
| A part of the already acquired owned Subject or iteration item | The stored complete Type `T` and its destruction responsibility, transferred; no `@move` is written |
| A Place reached with shared access | `ref/T`, also for a Copy `T` |
| A Place reached with exclusive access | `uniq/T`, when the path grants it |
| `_` | Nothing is acquired; the responsibility stays with the current owner |

On a path through a reference, parts are bound with that path's capability (§14.8.1) and are never Moved out of the referent: an owned item `ref/(A, B)` decomposes into shared borrows and `uniq/(A, B)` into exclusive borrows, and a Pattern that needs exclusive access through a shared path is rejected. Literal Patterns only observe the selected value. Owned decomposition distributes ownership that the Subject already holds; it grants no acquisition that §3.5 denies to a bare Place, and the responsibility for unbound parts stays with the Subject. Each selected Case identity and positional payload index of an owned Subject, including recursive decomposition, is tracked as a Move Path inside the Subject; these internal paths never apply to the caller's original enum and add no payload projection syntax. New borrows require valid referents and owners; copied references keep their existing Origins and Loans.

A change in payload Copy capability therefore changes neither binding Types nor generic-body validity: for `wrapper: Option<Result<i32, i32>>`, `match wrapper` with `.Some(let r)` binds `ref/Result<i32, i32>`, and `r@deref` Copies the value where one is needed.

**Guards.** In a guard, each candidate name is a shared reference `ref/T` to its selected Place, whatever the Subject mode and `T`'s Copy capability, held in its own reference slot with a Binding Identity distinct from the body binding. No payload is Moved during a guard; the body bindings are initialized from the original Places only after a true guard's cleanup. §14.8.3 owns guards, including their cleanup, candidate restrictions, escape checks and Subject protection.

**Lifetime.** The Subject lasts for the match evaluation. A match result or outward transfer value is secured first; then the arm scope is cleaned up under ordinary Scope Exit; finally the Subject's remaining initialized parts are destroyed. Body bindings enter scope left to right and are destroyed in reverse order, before the Subject; body locals and `defer` keep their normal reverse registration order. A Wildcard does not destroy immediately, and unselected arms acquire no body ownership. Each step requires the earlier cleanup to complete normally; Abort does not unwind.

A borrow through a borrowed Subject depends on the referent and the original Loan, not on the temporary slot that stores the reference, and may be returned when the original contract allows. A new borrow into an owned Subject's payload depends on the Subject and cannot escape the match. Copying or moving a stored external reference instead keeps that reference's external Origin.

## 15.2. Origin expressions and ordering

The basic meaning of Origins and Loans is given in [Origins and Loans: overview](03-types-and-values.md#37-origins-and-loans-overview). This section defines annotation expressions and their ordering.

### 15.2.1. Origin expressions

An Origin is the set of program points at which a borrow is guaranteed to be valid.

| Kind     | Examples                | Meaning                                         |
| -------- | ----------------------- | ----------------------------------------------- |
| Concrete | `x`, `self`, `x.source` | Origin carried by a parameter, receiver or local value |
| Abstract | `source`, `left`        | A scalar schema slot or an implicitly introduced signature Origin |
| Projection | `view.source` | A slot selected from a binding set or value Type |
| Static   | `static`                | Built-in maximum Origin                         |

```text
origin-expression := Name
                   | Name '.' Name
                   | static
                   | '(' origin-expression ')'
                   | origin-expression 'and' origin-expression
```

A direct safe-borrow parameter, receiver or local used as an Origin denotes its value's outer borrow Origin, never the lifetime of the variable's storage:

```kimi
func first<T>(x: ref/T) -> ref/T during x
```

`x.source` denotes the abstract Origin `source` carried by `x`. Qualification is required so that values of the same Origin-bearing Type remain distinguishable:

```kimi
struct View<T> {source}
    func get(self: ref/Self) -> ref/T during self.source
```

The same projection applies to Origin-bearing local values under ordinary lexical visibility; executable uses require definite initialization. A bare value name with no outer safe-borrow Origin is invalid as an Origin, even if its owned outer Type has borrowed contents; select a declared Origin with `x.source` instead. Thus, for a local `r: ref/T`, `during r` always denotes the Origin of the borrow that `r` holds, never `r`'s slot. Borrowing `r`'s slot uses an explicit layered Borrow (§13.5.5), whose slot Origin is inferred. Storage Origins of owned locals likewise remain compiler-internal and are inferred from initialization. Local values cannot supply Origins in public signatures or outside their lexical scope.

### 15.2.2. Ordering and intersection

`origin o1 outlives o2` requires `region(o1) ⊇ region(o2)`. The relation is reflexive and transitive, and `static` outlives every Origin. `origin o1 == o2` requires equal regions; it does not merge Loans or their referents.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

The intersection contains no region outside either operand and may equal an operand. A result declared `during (x and y)` is valid only in the region common to both inputs.

Intersections are normalized with these laws; an outlives simplification requires a proof under the [limited Origin solver](#1536-limited-origin-inference):

```text
a and b         = b and a
(a and b) and c = a and (b and c)
a and a         = a
a outlives b implies a and b = b
```

For a fixed binding and proof environment, an intersection is flattened; proven-equal or mutually outliving Origins are replaced by the least stable representative; duplicates and operands proven to outlive another operand are removed; and the rest is sorted. The sort uses a deterministic total order based on stable Binding Identity, including the declaration binder and parameter position where applicable, never spelling, input traversal order or memory address. A singleton intersection is its operand.

Intersections are renormalized after substitution or a change of proof evidence, and cached reductions must validate their proof dependencies. The result is a canonical form under the permitted rules, not a general semantic-equivalence test. Input Loan dependencies are kept separately: simplifying an Origin expression never removes a distinct input's Loan.

### 15.2.3. `static` and `Owned`

```kimi
func empty() -> ref/string during static
```

A shared borrow from `static` has no non-static lifetime dependency and must satisfy the [static-source rules](11-properties.md#1132-static-storage). A new safe borrow of mutable static storage has a finite Origin. Safe code cannot derive `uniq/T during static` from longevity alone, because an exclusive borrow also requires a unique Loan anchor; in safe code, an abstract Origin whose Loan requirement is `uniq` cannot be bound to `static`.

`static` describes an Origin. `Owned` expresses independence from non-static lifetime dependencies; it is neither ownership Semantics nor permission to allocate storage:

```kimi
func register<F>(f: F)
    F is Owned
```

A valid complete Type `T` is `Owned` exactly when every Origin in **OwnedOrigins(T)** equals `static`; an empty set satisfies the condition. OwnedOrigins is the conservative dependency closure of the Type's outer Origin; its Semantics target (value referent, object payload or View Target, or raw-pointer pointee Type); all instantiated Type and Origin arguments, including unused slots; bases; stored Fields; enum payloads; Tuple components; array elements; and concrete Closure captures. Aliases are expanded and declaration bindings substituted before traversal. Recursive Types use the structural fixed-point rules, not circular conformance evidence. A base or runtime-Contract view contributes its visible Type and Origin arguments; its hidden payload was certified at erasure (§15.8.1).

The OwnedOrigins of a nested Type include inherited explicit Origins and unused outer Type arguments (§6.1.3), so an empty `Outer<ref/i32 during local>.Tag` is not Owned. These Type-level dependencies imply neither a retained outer instance nor an actual Loan. Static storage uses the shared key of §22.2.4 without erasing full-reference lifetime checks.

A callable Type contributes every fixed Origin in its complete Type: a Function Item's bound generic and Origin arguments, a concrete Closure's captures and fixed signature Origins, and the fixed Origins written in a common Function Type's parameter and result Types. Only Origins bound per call, such as the direct-input quantification of §8.6 and §15.4, are excluded, because they have no fixed binding to prove. A common Function Type's hidden environment is certified Owned at erasure. An Owned proof never infers or rewrites a callable's per-call contract.

Established outlives facts are used: `a outlives static` proves `a` equal to `static`, the maximum Origin. An unbound or unproven abstract Origin yields Unknown, not a proof of `not Owned`; required evidence is resolved by the ordinary deadline. A generic definition proves Owned for its own Type parameters and abstract Origins only from its declared Constraints and bounds (§8.10); the proof cannot wait for instantiation. Empty containers and unselected Cases do not weaken this Type-level check: `unsafe/(ref/i32 during local)`, and a wrapper with that non-static Type argument, cannot prove Owned even if no safe-borrow Field is visible. Traversing a pointee Type neither dereferences a pointer nor creates a Loan; unsafe implementations must still expose their actual lifetime dependencies and uphold pointer validity.

This revision requires Owned for static storage, concrete payload erasure into base or runtime-Contract views, common Function Type environment erasure, cyclic-factory payloads (§13.5.8) and explicitly declared Owned Constraints. A lifetime-hiding library API states that requirement explicitly; the compiler does not infer an "indefinite retention" capability from a private body. Ordinary storage and concrete object allocation impose no blanket Owned requirement. Owned never discharges acquisition, Loan, destruction-order, unsafe or concurrency checks.

## 15.3. Origin schemas, names and relations

The Origin system expresses lifetime dependencies with four concepts: an **Origin**, a **binding set** that maps a Type's schema slots to Origins, a **projection** that selects a slot, and a **relation** between Origins. Infer when possible; relate when necessary; name only when useful; declare when needed. There are no function Origin parameter lists and no Type Origin mappings, but the semantic binders needed for universal contracts remain.

```text
Type schema: Pair.left, Pair.right
└─ Type occurrence: Pair<A, B>{p}
   ├─ p.left  → Origin α
   └─ p.right → Origin β
      Relations constrain regions; separate Loans retain Places and authority.
```

### 15.3.1. Borrow annotations and binding sets

```kimi
ref/T during source                    // Origin of this borrow layer
View<T>{v}                       // Name this Type occurrence's binding set
ref/View<T>{v} during borrow           // Outer borrow and inner slots are distinct
```

Borrow annotations use postfix `during` with the attachment and order of §3.3.6. Only `ref`, `uniq`, `objref`, `objuniq` and a Semantics parameter whose [admitted set](08-generics-constraints-and-contracts.md#87-constraint-proof-system) lies within the `borrow` category accept them; `owner`, `obj`, `rc`, `arc` and `unsafe` do not. Whole-Type parentheses do not accept an annotation from outside.

The argument is one Origin atom: a simple name, `value.slot` or `set.slot`, `static`, or a parenthesized Origin expression. An intersection after `during` must be parenthesized: `during (a and b)`. A parenthesized single atom is valid; empty parentheses, lists, trailing commas, `_`, calls and arbitrary value expressions are not. In `f(x: ref/T during a,)`, the comma belongs to the parameter list.

An outer `and` belongs to the surrounding grammar: `T is ref/U during a and Copy` is a requirement conjunction. In a Type-only position, `ref/T during a and b` is invalid and is never reparsed by lookup. Relation clauses accept unrestricted Origin expressions on either side of `outlives` or `==`; these operators and the clause end delimit the operands. An annotation creates no Loan, extends no lifetime, and performs no acquisition, conversion or Reborrow.

A named Type reference's `{name}` introduces one binding-set name, with an optional trailing comma. It never applies an existing Origin or set. The Type must have a nonempty schema known at definition; unknown generic schemas, duplicate names and use of a set as a scalar Origin are errors. Name each required occurrence separately and relate its slots. There is no whole-set equality, positional Origin application, mapping such as `{source => x}`, or call-site application such as `f{a}(...)`. `_` is neither a binder nor an inference request. Empty braces are permitted only on Type declaration headers (§15.3.2).

```kimi
func identity<T>(value: View<T>) -> View<T>{result}
    origin result.source == value.source
    return value
```

Naming preserves the Type, dependencies and quantification. Adding or removing a valid unused set name, or consistently renaming it and its references, leaves the contract unchanged. Naming an already complete Type does not reopen its bindings. Prefer a value projection when it directly names the needed slot; set names remain available for results, nested occurrences and other Type expressions.

The role of braces follows from syntactic position, independent of whitespace or lookup success: in a declaration they are a Type schema header; after a named Type they name a set. A following `/` does not turn a set into a borrow annotation. Brace borrow annotations before `/`, Origin lists on functions, constructors and accessors, `origin`/`from` borrow annotations, and brace contents of the wrong role are rejected.

**Projection.** `value.source` selects a declared slot from the value's Type (§15.2.1): after aliases and redundant owner prefixes are normalized, only consecutive safe-borrow layers are peeled to find that schema, never Fields, Type arguments or raw pointers. A set projection selects the corresponding slot of its named occurrence. Unknown slots are errors. Projection reads compile-time Type information; it invokes neither dereference nor getters and grants no initialization or Loan permission.

### 15.3.2. Type schemas and storage

Structs and enums can declare their own Origin slots in a header after the generic parameters and before the base clause. A header lists simple fresh Names, allows a trailing comma, and contains no bounds, `static`, projections or intersections. Duplicates and redeclarations of inherited names are errors. Groups and Contracts declare no own slots and keep their enclosing environment.

| Header | Own slots |
| --- | --- |
| Absent | At most one distinct scalar Origin is inferred from directly written storage borrow annotations. |
| `{source}` or `{left, right}` | Exactly the listed slots; no implicit additions. |
| `{}` | No own slots; no implicit additions. Inherited and complete-Type dependencies remain. |

A written header **closes the schema**. Without one, simple Origin names are collected from the whole selected storage schema: instance Fields, enum payloads and bases, including borrow annotations written inside Type arguments. Repeated occurrences of one name are one candidate; two different candidates reject the whole Type, whatever the traversal order. `static`, set names, inherited dependency metadata and dependencies already bound inside a complete Type argument are not counted, and collection does not cross nested declarations or callable boundaries. This limit does not restrict a function's scalar or anonymous input Origins.

```kimi
struct View<T>
    public let value: ref/T during source

struct Pair<A, B> {left, right}
    public let first: ref/A during left
    public let second: ref/B during right

struct Typo
    let first: ref/i32 during source
    let second: ref/i32 during souce // Error: two implicit candidates.
```

In headerless storage, a simple name is an own-slot candidate; a collision with a visible inherited Origin is an error, not an implicit capture. A Type whose storage directly references inherited Origins requires a closed header, `{}` if it adds no slots. In a closed Type, references resolve to its declared slots or to the visible enclosing Origins.

```kimi
struct Outer<T> {source}
    struct Inner {}
        let value: ref/T during source
```

Every fragment of a split struct repeats the same closed header, including `{}` for zero own slots; slot count, order and names agree under §6.1.2. Type relations occupy the same unique Constraint definition region as other Type Constraints, which the other fragments share. Split groups gain no Origin header.

**Storage completion.** Every stored borrow and aggregate slot is completed from public Origins, `static`, complete Type arguments and explicit annotations and relations. Initializers and constructor assignments must satisfy this contract; they never infer it. No hidden free slot is synthesized, and a nested schema is never flattened into the containing Type.

```kimi
struct Wrapper<T> {source}
    let value: View<T>{inner}
        origin inner.source == source
```

`inner` is local to that Field and its attached clauses. Any remaining Field condition must follow from the containing Type's public premises and intrinsic well-formedness. Additional public premises belong explicitly in the Type's Constraint region; private Field clauses cannot silently add them. The same rule applies to every enum payload, whatever the selected Case.

A complete Type has an established contract, including its Origin bindings and quantification; its Origins need not be concrete regions. A schema includes lexically inherited slots but does not flatten the dependencies of Type arguments, Fields or bases; those remain in their complete Types and in OwnedOrigins (§15.2.3). An empty schema alone does not prove Owned.

### 15.3.3. Declaration-attached relations

```kimi
origin a == b
origin a outlives b
```

Each clause contains one relation; multiple clauses are conjunctive. Both operands are Origin expressions (§15.2). `origin` and `outlives` are contextual words here. Relations introduce no names. There are no chained comparisons, disjunctions, negations or runtime tests.

A clause is indented once under its declaration. Functions and Types place it in their leading Constraint region. Fields, locals, enum Cases, associated-Type specifications and Container aliases attach clauses to the declaration, before any accessors or other bodies. Syntactic attachment determines the owner, and referenced names must be visible there. A clause never moves to the innermost referenced declaration, and there are no constraint blocks on arbitrary expressions.

| Owner | Obligation |
| --- | --- |
| Public function or Type contract | Publish premises and check the definition for every admitted binding; prove substituted premises at each use. |
| Field, payload or associated-Type specification | Bind the selected Type and prove remaining conditions from the enclosing public contract. |
| Base Type | Bind the header occurrence; publish additional premises explicitly in the Type's Constraint region. |
| Local or fixed Container alias | Check against established evidence and the initializer, where present; assume no new facts. |

A closed condition independent of declaration parameters is a definition-time proof obligation; a false condition cannot make an invalid definition vacuously acceptable. Type-intrinsic well-formedness remains separate from arbitrary Field preconditions and is preserved even for anonymous slots.

### 15.3.4. Names, scope and quantification

In an ordinary named function, constructor, explicit accessor or Contract callable requirement, an unbound simple name in a permitted signature borrow annotation introduces a universally quantified scalar Origin. Relations and local annotations only reference existing names. Specializations and inherited stored accessor positions retain the original contract without adding binders. Input and result completion follow §15.4.

```kimi
func nested<T>(x: ref/(ref/T during s), y: ref/T during s)
func constant() -> ref/i32 during s
// Result-only s is universal too; a local referent cannot satisfy it.
```

| Name owner | Visibility |
| --- | --- |
| Callable signature Origin or set | Signature, clauses and body. |
| Type scalar slot | Type declaration and inherited member environment. |
| Field, enum Case or associated specification set | That declaration and its attached clauses only. |
| Base occurrence set | Type Constraint region; not a new member-visible slot. |
| Local set | Its Type, attached clauses and the following ordinary local scope. |
| Container alias set | Attached clauses and that SourceDocument's alias scope. |

Lookup searches lexical scopes from the inside out and stops at the first scope with an Origin-context candidate. A same-scope conflict among value, Origin and set roles is an error. A wrong-role match neither falls back outward nor becomes a fresh implicit name. Bare Fields are not value-Origin candidates without a receiver. Type-parameter names keep their separate namespace.

A new Origin or set name cannot hide a visible Origin, set or competing parameter or local name. Duplicate set declarations are errors; mutually invisible Field-local names may be reused. Ordinary value-to-value shadowing is unchanged. Signature and schema names are collected before resolution, independent of input, Field or file enumeration order; Field-local sets are not hoisted, and executable visibility and capture boundaries do not change. Member bindings are rechecked when an enclosing schema changes.

**Nested signatures.** Function Types, Callable and anonymous functions introduce no new named scalar Origins; they keep their limited per-call direct-input quantification and their own result omission rules. A set name inside a Function Type or Callable belongs to the declaration containing that Type expression. Each nested aggregate input slot must be fixed by a complete Type or by an expression over existing outer Origins; an upper bound alone cannot leave a free per-call aggregate slot.

```kimi
func useView<T>(x: View<T>, callback: (View<T>{c}) -> ())
    origin c.source == x.source
```

`c.source` is fixed by the outer call, not selected again for each callback invocation. Callable permits this fixed set naming but gains no written direct-borrow Origin annotations. A nested result first keeps its fixed bindings and then uses its own signature's result elision: `(ref/T) -> View<T>` can inherit the inner per-call input. An unused set label does not change that contract. Inner per-call Origins cannot be projected or captured outside their binder; slots referenced by outer clauses must instead be completed with fixed outer Origin expressions. This boundary applies at every nesting level. Anonymous whole-result inference, expected Types and captures keep their existing rules.

### 15.3.5. Variance, Loan requirements and Phantom Origins

Origin relations are not value conversions. The complete-Type variance rules apply: `ref/T during o` is covariant in `o` and `T`; `uniq/T during o` is covariant in `o` and invariant in `T`; function parameters reverse polarity and results preserve it. Mutable storage follows its representation's invariance requirements. Declaration variance is inferred from all occurrences, and recursive Types are solved to a fixed point; there are no explicit variance annotations. These rules add no ordinary inheritance upcast or callable value operation.

Slots keep inferred Loan requirements `none < ref < uniq`: a shared borrow use requires `ref`, an exclusive use requires `uniq`, and multiple or nested uses propagate the stronger requirement. The requirement identifies the caller-side Loan that must be retained; it neither grants a Loan nor changes structural Copy classification. Actual Place, authority, anchor and Reborrow identities are kept across acquisition, storage, calls, results and destruction. Equal or shortened Origins never merge distinct Loans or manufacture exclusive access.

```kimi
struct RawView<T> {source}
    let pointer: unsafe/T
    let count: isize
    func get(self: ref/Self, index: isize) -> ref/T during self.source
```

A header slot without a corresponding safe stored reference is a **Phantom Origin**. Its dependency is retained, but its declaration grants no pointer validity, Loan, Copy or access authority. Unsafe implementations or verified intrinsics must establish initialization, bounds, alignment, permissions and retention. Required input-derived Loans stay attached to dependent values. A general phantom slot is invariant when safe shortening cannot be established structurally; verified intrinsic Types keep their established metadata. Phantom authority is never inferred. Static Origins still obey the source and unique-anchor restrictions of §15.2.3.

### 15.3.6. Limited Origin inference

Temporary inference variables are placed only at unresolved positions; fixed Origins never become variables again. Constraints are collected recursively from complete-Type variance, explicit relations and use requirements.

| Position | Principal candidate |
| --- | --- |
| Covariant | Unless fixed by equality, the meet of all upper bounds: their longest common region. |
| Invariant | Proven equality; never replace invariant inner Origins with a meet. |
| Contravariant | A lower bound proven to outlive every other lower bound. Do not invent unions of incomparable bounds. |
| Mixed or cyclic | A representable, unique principal solution modulo proven Origin equivalence. |

A principal solution is the most general permitted solution under variance and fitting. Multiple shorter regions do not make a covariant meet ambiguous. Each candidate is substituted into every condition, including internal dependencies and Loans. If no principal solution is expressible, or only incomparable candidates remain, an explicit annotation is required.

The solver uses equality substitution, reflexivity, transitivity and the meet laws, and keeps composite expression nodes: `a and b outlives c` decomposes into two requirements, whereas `x outlives a and b` decomposes into neither atomic edges nor a disjunction. Identical normalized premises and consequences of the permitted rules are usable. The solver neither enumerates arbitrary regions nor requires general theorem proving. An unknown proof remains distinct from a contradiction; unresolved obligations are errors at the existing finalization deadline unless §8.10 explicitly permits deferral.

### 15.3.7. Canonical contracts and verification

A canonical contract retains normalized complete Types, binders and scopes, fixed bindings, activation conditions, relations, intrinsic well-formedness, and Loan requirements and dependencies. For example, `ref/View<T>{v} during borrow` requires `v.source outlives borrow`. Intrinsic conditions are definition premises and use-site obligations, not access permissions.

Schema slots have stable declaration-bound identities; distinct declarations with the same spelling remain distinct. An anonymous input is identified by its declaration, input position, normalized Type occurrence and target slot. Grouping and redundant owner prefixes create no new slots. Recursive Types establish finite schemas before dependency and variance fixed points are computed; instantiation never discovers infinitely expanded anonymous slots.

Contract equality compares corresponding normalized structure, quantification, conditions, bindings and guarantees. Origin or set spelling, or explicit versus implicit declaration, alone distinguishes neither overloads nor specializations. Public Type slot names are still API: adding or renaming them, or changing their relations, affects projection clients even when their source Fields are private.

Compatibility keeps the owning feature's Type-structure rules, admits every call allowed by the requirement and provides at least its result guarantees. Required universal Origins are rigid arbitrary symbols; only instantiable call Origins of the implementation may be solved. Fixed Types and captures stay fixed. Inputs are checked as `required <: implementation` and results as `implementation <: required`; implementation conditions are then proven from the requirement's premises, never from the obligations themselves. Receivers, environments, authority and Loans are checked separately, for every admitted Semantics condition. Specializations and stored accessors inherit their original complete contracts. Contracts and proof dependencies are preserved through function references, artifacts and reload.

Implementations should share normalized schemas separately from occurrence bindings and reuse interned structures, stable IDs and scratch buffers; caches must be invalidated when bindings, premises, activation conditions or proof dependencies change. A closed header establishes slot names, count and identity early, but not variance, Loan requirements, Copy or layout independently of storage. Union-find and strongly connected components can help with atomic relations, but composite conditions and fitting are not mere graph reachability. An always-materialized transitive closure, which may need quadratic space, is not required. Use-site Loan, initialization and access checks remain necessary. Origins add no runtime arguments or lifetime tags and do not by themselves duplicate generated code (§21.3).

## 15.4. Origin completion and elision

Contracts are completed at definition: collect declarations and occurrences; inherit complete Types; resolve names and projections; normalize annotations and explicit relations; complete the remaining omitted positions; retain the result for body verification and use-site inference. Naming a binding set alone does not suppress omission. A named function's contract is never inferred from its body.

### 15.4.1. Explicit result relations

For an ordinary callable's aggregate result slots:

1. Keep established complete or inherited bindings, and positions determined by an explicit annotation or by equality substitution to an existing Origin expression.
2. Universally quantify the still-unbound result slots that remain in nontrivial outlives relations after the **explicit `origin` clauses** are normalized, subject to those relations. Upper bounds, lower bounds and composite relations are all permitted.
3. Apply result elision (§15.4.3) to the rest. Equalities that connect only unresolved result slots form one unresolved equivalence class, and the defaults of all its positions must agree.

Intrinsic Type well-formedness alone does not turn an omitted result slot into a new universal binder; it is checked against the completed contract. Equality and mutual outlives are normalized while any established binding is kept; nonsubstitutable equalities are kept as both outlives directions, and cyclic expressions are never expanded infinitely. Tautologies such as `static outlives x`, `x outlives x` and `x == x` do not change omission or quantification. A condition is never proven trivial by assuming that same condition. Normalization is deterministic and independent of clause order, but promises no general semantic-equivalence test.

```kimi
func shorten<T>(value: ref/T) -> View<T>{r}
    origin value outlives r.source

struct Marker {source}
func makeMarker() -> Marker{r}
    origin static outlives r.source // Redundant; source defaults to static.
```

A directly annotated result-only scalar name, such as `ref/T during s`, remains an explicit universal contract. No dedicated syntax universally quantifies an otherwise unconstrained aggregate result slot. Locals, storage and nested signatures follow their own completion rules. Completion occurs once: substitution may renormalize expressions but never reruns elision or changes binders when a condition becomes trivial. A relation on a complete Type checks its binding rather than rebinding it.

### 15.4.2. Position rules

Aliases, grouping and redundant owner prefixes are normalized first. These rules apply to aggregate slots and safe-borrow layers; `unsafe/T` gains no borrow Origin.

| Position | Omitted Origin |
| --- | --- |
| Direct borrowed parameter or receiver | Independent input Origin for that input's outer layer. |
| Aggregate in an ordinary callable input | Independent universal Origin for each unresolved slot, after inherited contracts and explicit relations. |
| Function result | §15.4.3; a named function omitting its whole result Type returns Unit. |
| Local with an initializer | Infer from initialization, expected Type and ordinary fitting/use constraints (§15.4.4). |
| Local without an initializer | Require a complete contract from explicit information and relations. |
| Instance Field or enum payload | Complete the storage contract explicitly (§15.3.2). |
| Static storage | Require Owned and §11.3.2's static-source rules. Omitted shared borrows and `none`/`ref` aggregate slots default to `static`; exclusive layers or `uniq` requirements are rejected. |
| Accessor or specialization | Inherit the complete stored/original contract first; complete only remaining positions. Getter results use the actual getter receiver. |
| Adaptation Target | Infer from the operand, operation and constraints, not signature result elision (§13.5.1). |

Ordinary input completion recurses through Type arguments, Tuple and array elements and borrow targets, not through Fields, already complete Types or another callable boundary. Distinct inputs, occurrences and slots remain independent; nested borrow layers gain no new omission permission. Constructors keep the containing Type's result contract; parameters do not automatically correspond to Fields. Original pair WholeTypes `s/T` keep their bindings; a reconstructed `s/U` keeps conditional safe-borrow slots under §8.1.2. Function Types, Callable and anonymous functions keep the boundaries of §15.3.4.

### 15.4.3. Result defaults

After explicit and inherited completion, apply these rules independently to each remaining omitted borrow-layer Origin and aggregate slot:

1. If any direct borrowed inputs exist, including borrowed receivers, use the meet of **all their outer Origins**. Never add aggregate-internal Origins as candidates.
2. Otherwise, shared borrow layers default to `static`; exclusive layers require an explicit valid contract.
3. With no direct borrowed inputs, an aggregate slot defaults to `static` only when every input Type is provably Owned from existing premises (vacuously true for no inputs) and its Loan requirement is `none` or `ref`. Otherwise require an explicit contract.

```kimi
func first<T>(x: ref/T) -> ref/T                 // x
func choose<T>(x: ref/T, y: ref/T) -> ref/T     // x and y
func view<T>(x: ref/T) -> View<T>                // source = x
func invalid<T>(x: View<T>) -> View<T>           // Error: result relation required.
func inner<T>(items: ref/Array<View<T>{v}>) -> View<T>{r}
    origin r.source == v.source
```

Conditional direct-borrow inputs are evaluated under every admitted Semantics condition. Elision never adds an implicit Owned constraint or weakens fixed dependencies to succeed. An Origin-compatible result still requires valid Type formation and actual Loan authority.

For `func f<T>(x: ref/T? during a) -> ref/T`, `x` is an Option rather than a direct borrowed input, so the shared result defaults to `static`; dependence on `a` requires an explicit result `during a`. If this default causes a result fitting or lifetime error, the diagnostic explains the omitted-input boundary and suggests visible explicit Origins without choosing among multiple candidates. A function correctly returning `static` needs no warning.

### 15.4.4. Locals and independent Type expressions

Local inference permits ordinary shortening and preserves variance, outlives and Loan obligations. The declaration fixes its Type and inference variables; later uses constrain them without reopening inference, and later assignments must satisfy the same contract. Genuinely omitted positions require an initializer. Non-generic omissions are resolved before body Origin and Loan analysis completes; permitted generic obligations are resolved by their established deadline. Uncertainty never becomes an invented `static`, a fresh universal Origin or erased dependencies.

```kimi
let view: View<T> = makeView(value)
    origin view.source == value

var pending: View<T>{p}
    origin p.source == value
```

The attached clauses are collected together with the Type annotation before initialization is checked. In its own attached clause, `view.source` refers only to the declaration's Type slot, not to an initialized value. Initializer lookup keeps ordinary visibility; `let x = x` still refers to an outer `x`. Without an initializer, equalities or established proofs must determine every slot as an existing Origin expression; an upper bound alone or a later first assignment is insufficient. Definite initialization is unchanged.

An independent Type expression is bound by attaching relations to its containing declaration:

```kimi
struct Derived<T> {source}: Base<T>{b}
    origin b.source == source

associate Source.Element is View<T>{e}
    origin e.source == source

alias StaticHelpers => Family.Helpers{h}
    origin h.source == static

let result = consume<View<T>{argument}>(view@move)
    origin argument.source == view.source
```

These examples assume the appropriate existing open base, associated-Type requirement and inherited group schema; their non-Origin access, role, construction and lookup rules are unchanged. Aliases remain Container aliases, not general or generic Type aliases, and cannot reference runtime values or another file's bindings.

Set names in source-written initializer Type expressions, including explicit Type arguments, construction qualifiers and Adaptation Targets, belong to the local declaration. Collection neither enters nested functions or declarations nor discovers names inside inferred Types, and nested callable Type expressions follow §15.3.4. Expressions without attached clauses can use ordinary contextual inference; an intermediate declaration is needed for extra relations. Leading function contract clauses cannot name arbitrary body Type occurrences. A binding set on an intermediate Container qualifier is written `(Outer<T>{o}).Inner<U>`, and the qualifier's dependencies are validated even when they are absent from the final Type.

Ordinary storage, including Arrays, Dictionaries, Tuples, fixed arrays, structs, enums, concrete object payloads and Closure environments, preserves complete Types and actual Loan identities through acquisition, Move, Copy, calls and destruction, and has no blanket Owned requirement (§15.2.3). Heap placement does not extend a referent's lifetime. Loan liveness follows required uses and observable destruction (§15.6), not merely lexical scope.

## 15.5. Exclusive origins

An exclusive borrow requires both a valid Origin and a unique Loan anchor: an Origin proves longevity but not uniqueness.

The following independent member signatures are declared inside `View<T> {source}`, with bodies omitted. A shared borrow may be returned from a stored Origin:

```kimi
struct View<T> {source}
    func get(self: ref/Self)
        -> ref/T during self.source
```

Returning `uniq/T during self.source` from `self: uniq/Self` is invalid, because detaching the result from the current `self` Loan could allow a second exclusive borrow:

```kimi
struct View<T> {source}
    func bad(self: uniq/Self)
        -> uniq/T during self.source       // Error
```

One valid form consumes the Origin-bearing owner; moving `self` prevents reuse of the capability:

```kimi
struct View<T> {source}
    func into_uniq(self: Self)
        -> uniq/T during self.source
```

Alternatively, the result Reborrows through the current exclusive receiver; while the returned Reborrow is live, the parent Loan stays active and access through it is suspended:

```kimi
struct View<T> {source}
    func get_uniq(self: uniq/Self)
        -> uniq/T during self
```

## 15.6. Borrow checking

Function bodies are lowered to a control-flow graph. A **program point** is a position immediately before or after an operation. The lowered representation uses these [Place](03-types-and-values.md#34-values-places-and-storage) projections:

```text
place := local
       | place '.' FieldIdentity
       | place '.base' BaseIdentity
       | place '.' TupleIndex
       | '*' place
       | place '[' _ ']'
```

These projections describe direct Field and lowered storage Places. Base and Field identities preserve inherited paths; no ordinary base-reference conversion is implied. Custom, computed and required accessors are function boundaries instead (Chapter 11).

A **region** is a set of program points. Local regions are inferred, Origins in signatures introduce universal regions, and `static` is the maximum region.

A Loan is:

```text
Loan = (place, mode, region)
mode = ref | uniq
```

A Loan is active at program point `P` exactly when `P` belongs to its region. Regions follow actual uses rather than lexical scope, which gives non-lexical lifetimes:

```kimi
let r = x@ref
use(r)
x.mutate()       // Allowed: r is no longer live; the receiver is acquired exclusively (§7.3).
```

### 15.6.1. Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `a outlives b` requires `region(a) ⊇ region(b)`.             |
| Well-formedness | Every Origin in `T` observable through `ref/T during o` or `uniq/T during o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described in §15.6.4. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

### 15.6.2. Place overlap and conflicts

Two Places overlap when an operation on one may affect the other. Static Place analysis proves non-overlap using only these structural rules:

| Places | Result |
| --- | --- |
| Identical Places, or a Place and an inline subpart | Overlap |
| Independent local roots and their inline parts | Disjoint |
| Distinct inline stored fields, Tuple elements or different constant fixed-array indices of one aggregate, and their subparts | Disjoint |
| Referents of simultaneously live valid `uniq`/`objuniq` borrows with distinct Loan anchors | Disjoint by exclusivity |
| Other reference dereferences | Follow Loan provenance and apply these rules |
| Anything not decided above | Non-overlap unproven; operations requiring a proof are rejected |

Inline parts exclude pointer and reference referents. Distinct shared-reference or raw-pointer variables alone do not prove independence. Constant fixed-array indices follow only the [ConstantIndexExpression rule](#1513-move-paths-and-partial-move) and compare decoded in-range literal values; runtime index comparisons such as `i != j`, integer proofs and optimizer results establish no disjointness. Array-derived Slices keep the whole-array Loan footprint through reslicing, splitting and empty views under the [Slice lifetime rules](04-arrays-indexing-and-slices.md#465-slice-storage-lifetime-and-permissions). Simultaneous exclusive borrows may be used only through their valid access paths, and Reborrowing still suspends conflicting parent access.

These are storage rules, not permission to bypass Property accessors. Direct Field operations may borrow disjoint fields separately, while custom, computed and required calls keep their receiver footprint.

Each operation is checked against every active Loan on an overlapping Place:

| Operation               | Existing `ref` | Existing `uniq` |
| ----------------------- | -------------- | --------------- |
| Read                    | Allowed        | Forbidden       |
| Write or Move           | Forbidden      | Forbidden       |
| Create `ref`            | Allowed        | Forbidden       |
| Create `uniq`           | Forbidden      | Forbidden       |
| Destroy the borrowed Place | Forbidden   | Forbidden       |

This permits shared aliasing or mutation, never both at once.

### 15.6.3. Reborrowing and region splitting

Borrowing through an exclusive borrow creates a child Loan anchored on the referent Place, with a region contained in the parent's Origin. While the child is live, the parent Loan stays live but access through the parent reference is suspended; overlapping access is rejected by the normal conflict rules. The child does not depend on the lifetime of the variable that holds the parent reference, so a Reborrow of a parameter may be returned or stored like a copied reference.

```kimi
func bump(n: uniq/i32)

var v = 0
bump(v@uniq)
bump(v@uniq)
```

Each call's temporary exclusive borrow ends before the next call starts.

**Region splitting** derives several child Loans from one parent Loan by the same mechanism, so each child's validity depends on the parent Loan and the owner. Exclusive children and the remainder of the parent region must be proven pairwise non-overlapping; shared children may overlap. Non-overlap is proven by the structural rules of §15.6.2 or by the verified operations of the standard storage boundary (§22.1.2.5). An ordinary Slice Loan is never split automatically, and neither general integer proofs nor a runtime Loan ledger is required.

A split target must be valid initialized Storage with correct bounds, placement and provenance. The remainder's capabilities are updated before a child is published, and capabilities over an already published part are never regenerated from the remainder. Zero-sized parts are distinguished by logical position, not by address. A Dictionary separates one entry and then lends the key and the value with different modes; exclusive access to a key, which fixes the entry's identity, is never published. Destroying an iterator or a remainder handle does not end published child Loans. While a published child is needed, conflicting reads, writes, reallocation, Move or destruction of the original collection are rejected. Internal dependencies of elements and external effects are checked separately from non-overlap.

**Independence of published results.** A result may be retained across later calls on the same receiver when its Type does not depend on the receiver Loan of the call that produced it and its Loan anchors are the external source, the owner or a needed parent Loan, not the receiver or the callee's storage. The callee's published effects (§15.6.4; for an Iterator, the effect bound of §22.1.2.4) must not conflict with the Loans of results it published earlier. A generic caller uses the published upper bound of those effects and never reanalyzes a private body.

### 15.6.4. Calls and origin propagation

For a call, the compiler:

1. creates fresh regions for the callee's abstract Origins;
2. instantiates the parameter Types and checks argument subtyping;
3. proves the substituted declared outlives bounds against the caller's facts (§15.3), rather than assuming them;
4. instantiates the return Type;
5. recursively collects its Origin dependencies and Loan requirements;
6. creates the required caller-side Loans and keeps them active for the corresponding result regions.

This applies to direct borrow results and to nested aggregate results:

```kimi
func make(a: ref/A, b: ref/B) -> Pair<A, B>{p}
    origin p.left == a
    origin p.right == b
```

While the returned `Pair` is live, shared Loans on both `a` and `b` remain active. A dependency requiring `uniq` propagates an exclusive Loan. An exclusive Loan formed for an argument remains exclusive while a result depends on it, even if the result requires only `ref`; result requirements never downgrade an existing Loan. Origin equalities neither create nor release Loans. For example, `Text.tryFormat` and `FixedBuffer.intoText` retain the original array's exclusive Loan in their shared UTF-8 result ([formatting profile](utf8-formatting.md#2-text-operations)). The spelling `static` alone creates no parameter-root Loan, but it does not erase the storage anchors and conflicts of a borrow into static Field storage.

Receiver and argument protection begins when the Borrow or Reborrow is formed, in evaluation order. Eligible exclusive borrows start with the call reservation of §15.6.7; other Loans are active immediately. After activation, call protection lasts through the entire call, including callee cleanup, not merely until the callee's last use, and extends for dependent results. Intrinsics and collection methods follow the same rules. These checks supply the call-wide attribute proof of §21.5.5.

**Static call effects.** Each callable's summary lists the static Field identities it may access and its read, shared or exclusive borrow, write, replacement and destruction effects, including those of callees, defaults, lazy initialization and cleanup. The summary is compared with active caller Loans by the normal overlap rules. Borrowed results keep Field anchors and dependency paths: borrows of immutable sources may be `static`, whereas borrows of mutable sources keep a finite Origin under §11.3.2. Static allocation never permits replacing or destroying a borrowed current value.

Summaries distinguish first-access initialization effects from ordinary accesses. A live Loan anchored to a Field proves that the Field has completed initialization, so its initializer need not be counted again; it proves nothing about an unrelated Field that the callee accesses first. If a result may derive from several static Fields, every possible anchor is kept, whatever runtime branch is taken.

For example, if `let view = State.text@ref` borrows a mutable static string Field, `view` has a finite Origin, and `State.reset()` is rejected while `view` has a later use if `reset` may replace that Field; a shared Loan still permits read-only calls. The whole call is summarized conservatively, and favorable runtime branches need no special analysis. Recursive fixed points are computed before acceptance. Separately compiled and indirect calls use published validated summaries, or treat unknown effects as conflicting with every potentially affected active static Loan; clients need not inspect private bodies. Immutable anchors are kept for shutdown dependencies even after erasure, while borrows of mutable sources cannot cross an Owned boundary. FFI validity and aliasing obligations still apply.

These static and capture anchors are kept when [receiver-preservation effects](12-expressions.md#12442-effect-verification) are composed, even when `self` is not an explicit argument; one call may affect multiple roots. Published summaries and their dependencies follow §18.3 and §21.3.4.

### 15.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

```kimi
func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    return local@ref // Error: the local cannot satisfy the universal Origin x.
```

### 15.6.6. Destruction lifetime checking

The [destruction rules](16-scope-exit-and-destruction.md#163-aggregate-destruction-and-deinit) and [Scope Exit](16-scope-exit-and-destruction.md#162-scope-exit-destruction) determine responsibility and order. Destruction lifetime checking requires every Origin and Loan that destruction may observe to be valid at each such observation:

```text
DestructorUsePoints(value, origin) ⊆ region(origin)
```

Destruction that observes no Origin or Loan adds no lifetime requirement. Every user-defined `deinit` is assumed to observe all reachable Origins, even if its body does not use them, and the same check applies recursively to field destruction. No relaxation mechanism is defined.

```kimi
struct Logger {sink}
    let out: uniq/Writer during sink

    deinit
        observe(self.out)
```

Here `observe` accepts `ref/Writer`, and passing `self.out` shares the stored capability instead of extracting it. `sink` must remain valid during destruction, even if the `deinit` body were `()`.

### 15.6.7. Call borrow reservations

A **call reservation** delays exclusive access, not evaluation or lifetime protection. It belongs to one invocation's preparation and is not a value, Type, Semantics or Origin. No runtime lock, allocation or fallible activation is required.

**Eligibility.** A final exclusive Borrow or Reborrow that directly prepares a receiver or parameter starts a reservation of its target. This includes the explicit `@uniq`, `@uniq/T`, `@deref@uniq` and `@objuniq`; the implicit exclusive acquisition of a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-explicit-receivers)), including the exclusive Reborrow of a borrow value; the implicit exclusive Reborrow of a borrow value at a `uniq` or `objuniq` parameter (§10.2); permitted exclusive payload dereferences; and generic adaptations with that resolved effect. An assignment target starts no reservation (§13.7). Parentheses are transparent. The path from the root through standard field and element projections that call no user code, up to the invocation, is one preparation and ends at the lending point ([§3.4](03-types-and-values.md#34-values-places-and-storage)): in `holder.items.append(holder.items.length)`, the lending point is `holder.items` and the argument is read during the reservation, and the call equals `holder.items@uniq.append(...)` and `holder@uniq.items.append(...)`. A getter or another invocation on that path activates at its own call and acquires its own receiver under §7.3. Direct, indirect, Callable, generic, constructor and intrinsic invocations follow the same rule. The operation and overload are resolved first; reservation legality never changes candidate ranking or retries selection.

A directly written adaptation and its call-only Reborrow form one preparation chain and must not first activate an intermediate exclusive Loan. Other operand operations keep their own evaluation and Loans. Reservations do not pass through local or aggregate storage, Closure captures, nested invocations, or the results of `if`, `match`, `do` or other control-flow expressions; such expressions use the ordinary Borrow and Reborrow rules internally, and a later call adaptation cannot demote their already active Loans. An inner invocation activates its own reservations before its entry. Exclusive borrows outside an eligible preparation are active immediately. Compound assignment and indexing keep their own evaluation rules; lowering them to helper calls grants no new reservation.

**Reservation.** The target is evaluated and located once, with checks of initialization, declared access, mutability, complete-Type and Origin validity, and the existing exclusive authority. Its Place, and the owner and provenance needed to keep that location valid, are protected. Shared reads and shared Borrows through otherwise valid paths may coexist with the reservation; conflicting writes, Move, destruction, reallocation and independent exclusive operations or reservations are forbidden. Structural non-overlap follows §15.6.2; equal Origins establish neither equal Loans nor disjointness. A reservation is not a shared borrow and cannot manufacture exclusive authority.

An exclusive child reservation preserves its parent's authority and dependencies. It permits temporary shared inspection through an authorized parent path, but never re-enables independent access blocked by an already active ancestor Loan. Activation suspends conflicting parent access under the normal Reborrow rules. Object projection, Property permissions and ObjectCallCompatible requirements are unchanged.

**Preparation and activation.** For a method call, the receiver is located and adapted first; then explicit arguments are evaluated in textual order, and omitted defaults in parameter order (§7.2, §7.3). Defaults may temporarily inspect prepared reserved inputs through shared access under §7.2; they cannot use them as active exclusive references or retain a new inspection Loan, and those temporary inspection Loans end before entry. No other temporary is destroyed early merely to make activation possible.

After all inputs are prepared, every reservation must be able to be active simultaneously. The check covers the complete argument set, retained and returned dependencies, captures, static anchors and cleanup effects. A shared Loan may coexist during reservation only if it does not conflict at activation. Owned or Copy results can still retain Loans. Overlapping arguments that are both exclusive, or one shared and one exclusive, are rejected even if the callee would not use them. Activation is a static proof boundary before the logical callee entry, not an ordered sequence that permits transient aliasing. All call-wide and result-Loan rules then apply.

**Abandoned calls.** A control transfer keeps reservation protection while its operand is evaluated and acquired. Only the reservations of invocations abandoned by that transfer end, without activation, before the abandoned preparation's remaining cleanup. Actual Loans retained by other values, and all normal acquisition and cleanup ordering, are preserved. A transfer caught inside an argument does not abandon the enclosing call. Divergence enters no callee; Abort keeps its no-cleanup contract. Unreachable code is still checked under the ordinary rules.

```kimi
var p: i32 = 1
Kimi.Intrinsics.replace(p@uniq, with: p + 1) // Read during reservation.
Kimi.Intrinsics.exchange(p@uniq, with: p + 1)
// Kimi.Intrinsics.swap(p@uniq, p@uniq)      // Error: overlapping targets.

tasks.insert(tasks.length, Task.init(9))    // Shared read during the receiver's reservation.
holder.link.update()                        // link: uniq/T; the lending point is the referent, and link's slot is only protected.
// tasks.append(tasks.remove(0))            // Error: two implicit exclusive acquisitions overlap.
let moved = tasks.remove(0)
tasks.append(moved@move)
```

A result that depends on an exclusive receiver keeps the receiver's exclusive Loan while the result lives (§15.6.4):

```kimi
struct Inventory
    var items: Array<Item>
    func newest(self: uniq/Self) -> ref/Item during self
        ...

var inventory = makeInventory()
let item = inventory.newest()               // inventory stays exclusively borrowed while item lives.
// inventory.items.append(makeItem())       // Error.
inspect(item)
```

Diagnostics distinguish the reservation, the activation and the conflicting use or retained Loan. A Loan conflict at an implicit lending point carries a note naming the acquisition, such as "`tasks` implicitly borrowed exclusively as the receiver of `append`"; a result that retains the Loan is shown as well, and when two implicit acquisitions overlap, both lending points are shown. A diagnostic suggests a separate local only when the dependencies of the value it would hold allow the call.

<a id="157-initialization-preserving-exchange"></a>

## 15.7. Whole-value updates

The following ordinary declarations belong to the public `Kimi.Intrinsics` group (§22.1.1) and have compiler-intrinsic implementations selected by declaration identity; a same-spelled user function has no intrinsic behavior. `T` is any valid complete value Type; these operations impose no `Sealed`, `Copy` or `Owned` constraint.

```kimi
public func replace<T>(target: uniq/T ! with => value: T) -> ()
public func exchange<T>(target: uniq/T ! with => value: T) -> T
public func swap<T>(first: uniq/T, second: uniq/T) -> ()
```

Each target must be fully Initialized, exclusively writable and permitted to undergo a whole-value update, and its complete Type must match the incoming value exactly, including generic arguments and internal Origins. Ordinary complete owner storage may contain an open Core. Object payload storage instead needs the complete-target proof of §13.5.5.1; neither a base subobject nor an open object View qualifies.

| Operation | Old contents | New contents | Result |
| --- | --- | --- | --- |
| `Kimi.Intrinsics.replace` | Destroyed at the original location | Installed only after destruction completes | Unit |
| `Kimi.Intrinsics.exchange` | Transferred without destruction | The acquired value is installed | The old value and its responsibilities |
| `Kimi.Intrinsics.swap` | Both transferred without destruction | Contents and responsibilities exchanged | Unit |

The targets are arguments, not Receiver Expressions, so an owned Place is lent with `@uniq` whatever its access path, and an existing borrow value is Reborrowed without a spelling ([lending rule](#1515-movable-places)). An object payload is selected with `@deref` first (`handle@deref@uniq`); `@uniq` on a reference or handle borrows its slot (§13.5.5). If `T` is a borrow Type, the operations transfer its reference value and capability, not ownership of its referent. Property access and hidden-storage permissions still apply.

These operations cannot repair Uninitialized, Moved or partially moved storage; `=` keeps its own repair rules. They permit no incomplete MoveOut through a borrow, no unrestricted construction or destruction receivers, and no assignment to the immutable `self` binding. A whole-content replacement may replace a value containing `let` fields without granting individual writes to those fields.

### 15.7.1. Evaluation and transfer

Argument evaluation, target reservation and activation follow §15.6.7, including textual order for named arguments. Acquisition and fitting finish before any update. If an argument does not complete, no update occurs; earlier effects remain, and the ordinary cleanup and Abort rules apply.

`replace` destroys the complete old value in its original location in the normal `deinit`, field and base order. Abort or divergence during that destruction prevents placement, and no rollback is promised. Placement transfers the preconstructed new value without rerunning constructors, initializers or setters. The transfers of `exchange` and `swap` execute no user code, destruction or Abort-producing operation. Their internal empty state is unobservable, and no inter-thread atomicity is promised.

```kimi
var p: i32 = 0
Kimi.Intrinsics.replace(p@uniq, with: p + 1) // Copy the input before activation.
let old = Kimi.Intrinsics.exchange(p@uniq, with: 5)
p = p + 1                     // Valid: assignment evaluates its RHS first.
var q: i32 = 9
Kimi.Intrinsics.swap(p@uniq, q@uniq)
// Kimi.Intrinsics.swap(p@uniq, p@uniq)   // Error: overlapping exclusive targets.
```

For a Non-Copy `x`, `x = x@move` can transfer and reinitialize, whereas `Kimi.Intrinsics.exchange(x@uniq, with: x@move)` attempts to transfer reserved storage and is rejected.

```kimi
struct Game
    var a: Resource
    var b: Resource

    func play(self: uniq/Self)
        Kimi.Intrinsics.swap(self.a@uniq, self.b@uniq) // Places reached through self still need a spelling at argument positions.
        self.a.update()                                // A Receiver Expression does not.
```

### 15.7.2. Static non-overlap

`swap` requires its two targets to be proven disjoint by the structural rules of [Place overlap](#1562-place-overlap-and-conflicts). An unproven relationship is rejected, even when runtime comparisons or optimization suggest separation.

```kimi
func swapValues<T>(a: uniq/T, b: uniq/T)
    Kimi.Intrinsics.swap(a, b) // Valid live exclusive inputs establish distinct anchors.
```

### 15.7.3. Storage update dependencies

These rules apply to `=`, `replace`, `exchange` and `swap`. Preparation, acquisition, movement, destruction, placement and cleanup must preserve every Loan and Origin needed by incoming, returned or surviving values; moving contents can invalidate dependencies even when nothing is destroyed. The lifetime of storage is distinct from the lifetime of its current contents.

The operation's exclusive Loan and its parent chain are preserved. An update that would invalidate live borrows anchored to the old contents is rejected, and placing a new value at the same address never restores revoked dependencies. A result that borrows data owned by the old destination cannot survive destruction of that data. Independent external dependencies may survive: a Copy of a stored `ref/Data` may keep an external `Data` Loan, whereas borrowing that field's storage as `ref/(ref/Data)` depends on the field itself. The actual anchors are checked, not the surface syntax.

Updating a complete object payload preserves the object's Identity, allocation, Dynamic Type, descriptor, header and ownership mode. Payload destruction is not final object release: counts are not altered, the enclosing object's lifecycle state does not change, and its storage is not freed. Nested owning handles still follow ordinary destruction. The special receiver, reentry, view and resurrection rules apply during content destruction, and an empty or destroying payload is never exposed through an ordinary view. Final release destroys the *current* payload once and frees the object. An exchanged old value carries a separate destruction obligation.

Facts and projections about the old contents, including `let` fields, are invalidated, while valid storage access and object Identity and Dynamic Type refinements are preserved (§14.10.1). Handle replacement follows its own rules. Unsafe code and optimization obey the same distinction between dependency and allocation lifetime, and raw pointers cannot substitute for Loan evidence.

## 15.8. Captured and erased dependencies

### 15.8.1. Object payload erasure

Erasing a concrete payload behind a base or runtime-Contract view requires its complete data Type to be `Owned` under the OwnedOrigins closure of §15.2.3; the handle's own Origin is not part of this test. Payload Type and Origin arguments and the ordinary exclusive-Loan restrictions are resolved before erasure. A local object borrow may still have a local Origin when its payload is Owned. Creating an object from a value requires the payload Type to satisfy [ObjectPayload](08-generics-constraints-and-contracts.md#8472-objectpayload); erasure and later view changes of an existing handle re-prove nothing about its creation.

```text
Dog owns only i32/string data -> Owned payload -> base/contract erasure allowed
Dog stores a local ref       -> non-Owned     -> initial erasure rejected
objref/Animal during local     -> borrow remains local even when payload is Owned
```

This is not a blanket `static` Origin requirement on object handles or exact concrete views; same-target operations keep the existing lifetime rules. An erased view certifies that the check succeeded, and later upcasts and casts inherit that certification without runtime Origin queries. Only proof-covered fixed Origin bindings may be supplied as `static` in a checked cast (§13.6.2); per-call callable Origins and the handle's outer Origin are not reconstructed. Existing borrowed-field Types remain valid; hiding their non-static dependencies needs a later existential-view design (§15.9).

### 15.8.2. Closure dependencies and call results

A Closure recursively keeps every captured value's Origin and Loan dependencies, not merely the lifetime of its creation Block. Moving in an owned value with no borrowed contents does not borrow its old local storage. Copying a shared reference, moving an exclusive reference, Reborrowing, or acquiring a borrowed aggregate preserves the corresponding external Origins, child Loans and parent restrictions. Independent dependencies are never collapsed or discarded at generic substitution or Type erasure. OwnedOrigins (§15.2.3) applies to the captured Types when the environment is proven Owned.

A Closure's captured dependencies are distinct from each call's receiver and result dependencies:

| Result source | Contract |
| --- | --- |
| Borrow of environment-owned data | Depends on the current Closure receiver borrow |
| Copy of a captured external shared reference | Keeps its external Origin |
| Reborrow of a captured exclusive reference | Also depends on the current exclusive call Loan |
| External reference value moved out by a Consuming call | Transfers that reference's external Origin and capability |
| Borrowed argument | Follows the input/result Origin contract |

When the whole result Type is omitted and inferred from the body, its Origin and Loan dependencies are inferred too. An explicit result annotation or a fixed expected signature keeps the ordinary result-Origin elision: the hidden environment receiver is not a source-level `self` or a directly written borrow parameter.

```kimi
let text = makeText()
let get = func [text@move] () => text@ref
// Internal signature: call(self: ref/Self) -> ref/string during self.
let view = get()
let moved = get@move // Error if view is still used below.
inspectText(view)

let other = makeText()
let invalid = func [other@move] () -> ref/string => other@ref
// Error: annotated omitted result Origin is static, not the environment borrow.
```

Multiple results are joined under normal result validation, keeping all possible Loan dependencies when an Origin meet is taken. Invariant mismatches, cycles and unexpressible dependencies are rejected rather than weakened. A repeatable exclusive result keeps its call Loan until the needed uses end, which prevents conflicting reentry. No result may outlive call-local storage or borrow an environment consumed by that call; moving an external reference value out is different and may be valid.

The internal call contract is preserved for concrete generic use, and callers never inspect bodies to rediscover lifetimes. Common Function Types and Callable constraints may return input-dependent borrows but cannot expose results that depend on the hidden receiver (§8.6).

### 15.8.3. Escape and retention

Escape describes retention across a creation or call boundary, not a permanent syntactic category of Closures. Returned values, saved fields, other Closures and indirect callees are checked against the destination's lifetime and capability contracts. Borrow capture is not inherently non-escaping, and Move capture does not remove nested borrow dependencies. Moving or heap-allocating a Closure cannot extend a local referent's lifetime, and temporaries keep their original expiration.

Non-escaping means that the callee retains neither the callable nor its environment dependencies beyond the call; it does not mean a single call, no allocation or waived Loan checks. `ref/F` and an Owned result let a verified callback-only body use a borrowed concrete environment without erasure, but neither `ref/F` nor Callable alone promises that no environment-derived value can be saved. A separately compiled callee cannot be assumed non-escaping without a verified contract. Non-escaping declaration syntax and borrowed erased callable views remain deferred.

Loan validity covers later uses, results and dependencies observed by destruction, not just the last body invocation or the lexical end of a binding. Concrete Closure storage follows §15.4, and the Owned boundaries are listed in §15.2.3. Owned grants no thread-transfer or concurrent-access capability; see the [concurrency design boundary](appendices/D-deferred-features.md#d2-concurrency-memory-model-and-thread-transfer).

## 15.9. Lifetime design boundaries

This revision does not define:

- abstract Origin parameters owned by Contracts themselves; static Contracts may inherit outer Origins under §6.1.3, associated Types may declare Origin parameters under §8.4.3, and the [Property getter receiver and result contracts](11-properties.md#1122-computed-properties) introduce no Contract-level Origin parameters;
- existential object views that hide non-static payload dependencies;
- general higher-ranked Origins beyond the direct-input quantification of Callable constraints and the per-call `step` of `LendingIterator.next` and of other Origin-parameterized associated Types (§8.4.3.1);
- Origin expressions naming static Places, such as a function result bounded by a mutable static Field; use direct access, an input-bounded result (§11.3.2), a scoped callback or immutable static storage instead;
- cancellation cleanup guarantees.

These features require extensions of the [ownership and Origin rules](#15-ownership-and-lifetime-analysis) and must not be inferred from this revision.
