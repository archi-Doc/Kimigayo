# 15. Ownership and lifetime analysis

[Specification index](../SPEC.md)

## 15.1. Initialization and consume analysis

This section checks the initialization, completeness and Consume legality of the values defined in [values, places and storage](03-types-and-values.md#34-values-places-and-storage).

### 15.1.1. Storage, state, and responsibility

Initialization state and destruction responsibility are tracked per Place:

| State | Meaning | Read / borrow / Copy / Move | Write to `let` | Write to `var` |
| --- | --- | --- | --- | --- |
| Uninitialized | No initialized value is held | Forbidden | Only if never initialized | Initialization |
| Initialized | An initialized value is held | Subject to Type and access rules | Forbidden | Replacement |
| Moved | The former value or capability and its responsibility were transferred | Forbidden | Forbidden | Reinitialization |

Moved records the source's history, not whether the destination value is still alive. A `let` Place's first initialization is tracked separately; neither a Move nor internal destruction resets it. This revision has no general user operation that explicitly destroys a Place and resets that history. State alone grants no access or write permission.

Every read, borrow, Copy or Move requires initialization on every incoming Runtime Reachability path (§14.9.2). Unreachable source uses the separate checking state of §14.10.3; the absence of an execution path does not restore an unusable value. `let` permits one initialization per binding lifetime on each path, while `var` permits later initialization and replacement under ordinary permissions. Fields also obey their dedicated access rules.

```kimi
var number: i32
if condition
    number = 1
else
    number = 2
print(number)             // Both paths initialize number.

let resource: Resource
resource = makeResource()
consume(resource)         // Move.
resource = makeResource() // Error: let cannot be initialized again.
```

### 15.1.2. Aggregate construction and completeness

Two facts are tracked independently: **construction completion**, which records that initialization completed successfully, and **current completeness**, which requires every stored component to be Initialized and complete. A **complete value** satisfies both. For a derived structure, the components are its base subobject and its own Fields, and completion is tracked separately at each base/derived layer. For an enum, only the active Case's payloads are components, and [Case construction](06-declarations-and-containers.md#632-case-construction-and-resolution) commits completion; inactive Cases require no initialization. This revision has no optional-to-initialize stored fields. Computed Properties are not components and need no storage initialization.

Only the [constructor phases](06-declarations-and-containers.md#623-constructors) commit completion: success is validated, cleanup finishes with the storage alive, and completion is committed before the value is exposed or transferred. No user operation commits early or resets completion, and an incomplete exit or an Abort never commits. Initialized fields alone do not skip the remaining work. A complete field or base keeps its own completion and destruction responsibility even if its enclosing layer never completes.

Before completeness, whole-value reads, Copy, borrowing, Move and exposure are forbidden, including through ordinary accessors taking the whole `self`; direct operations on initialized fields follow their own access rules. After a completed construction followed by a Partial Move, direct [Field operations](11-properties.md#1112-move-paths-and-inherited-fields) are allowed; this does not authorize initial-construction access.

A Partial Move changes current completeness, not the construction-completion fact. Permitted reinitialization of all missing fields restores completeness without rerunning a constructor. If a field cannot be reinitialized, the value stays incomplete and cannot be used or transferred as a whole, although its remaining Initialized parts can still be used and cleaned up; no separate permanent-incomplete state exists. A whole-value Move transfers the construction information, and a whole replacement uses the new value's information.

Tuple and array construction place elements in increasing element-index order. Each element acquires its own initialization and responsibility only when its placement completes normally, and a partly built element is tracked recursively. The aggregate commits completion after all elements are placed. If an element expression leaves the construction by an ordinary control transfer, that transfer's result is secured first, and then the abandoned construction's remaining elements are cleaned up in decreasing index order. Previously moved arguments and completed side effects are not rolled back. Raw uninitialized memory is not an alternative safe construction syntax. Fixed arrays additionally require [whole initial construction](04-arrays-indexing-and-slices.md#43-initialization-and-inference); static-path repair after completed construction remains permitted.

### 15.1.3. Move paths and partial move

A **Move Path** is a statically trackable path with independent initialization state and destruction responsibility. The initial paths are Fields reached through statically known base-subobject paths, Tuple elements, fixed-length array indices recognized by the literal-only ConstantIndexExpression rule below, and combinations of these. Runtime indices, dynamic containers and user indexers never form paths, even with literal indices, and neither optimization-derived constant propagation nor arbitrary integer proofs extend the accepted paths.

**Constant fixed-array indices.** A ConstantIndexExpression is one nonnegative integer literal token, optionally enclosed in any number of grouping parentheses. All integer bases and digit separators allowed by §2.6 are accepted. Its value must fit the ordinary expected `isize` Type; separators are removed and the magnitude decoded by the lexical integer rules. Recognition involves no arithmetic, conversion, name lookup, general constant evaluation or target-dependent environment evaluation.

~~~ebnf
ConstantIndexExpression := IntegerLiteral | "(" ConstantIndexExpression ")"
~~~

After a fixed-array Type `[N of T]` is resolved, an index recognized this way forms a static element Move Path only when its value `n` satisfies `0 <= n < N`. Path identity uses the numeric value, so `1`, `0x1` and `(1)` designate the same element, and no optimizer result changes this classification. The same rule defines constant fixed-array indices for overlap analysis (§15.6.2); it grants neither a path through a dynamic collection nor permission to Move through a borrow.

| Index expression | Static fixed-array Move Path |
| --- | --- |
| `0`, `(0)`, `((0))`, `0x1` | Yes, when it fits `isize` and is in bounds |
| `1 + 1`, `-1`, `+1`, `3@isize` | No; operators and conversions are outside this grammar |
| `if condition => 1 else => 2`, an immutable Name, `^1` | No; selection, name propagation and from-end resolution are not literal recognition |

An out-of-range literal supplies no element path, but this alone does not make an otherwise valid evaluated index operation a compile-time error: its bounds check follows §4.6 and §17.3.4 and Aborts if executed. An operation that requires a statically eligible Move Path is rejected when no such path exists, independently of whether its bounds check could fail. Literal-fitting errors remain ordinary compile-time errors. An expression such as `array[1 + 1]` is therefore still usable for ordinary permitted reads but can gain neither Partial Move eligibility nor a disjointness proof from optimization.

Within an owned match Subject, selected Case payload positions also form Move Paths under [match acquisition](#1516-match-acquisition-and-lifetime). This exposes neither general payload access nor Partial Move from the caller's enum.

A Move Path defines tracking granularity, not access permission:

| Source access | Partial Move |
| --- | --- |
| Tuple element / constant-index fixed-array element | Direct Place acquisition normally Moves a Non-Copy value |
| Stored Property slot | Standard acquisition Copies `F` when it is Copy and otherwise Moves, under the permissions of §11.1 and the Move Path rules |

```kimi
var pair: (string, i32) = ("Alice", 30)
let name = pair.0@move  // Partial Move; pair is incomplete.
let age = pair.1   // Remaining initialized part is usable.
// let all = pair  // Error: incomplete.
pair.0 = "Bob"
let all = pair     // Complete again.
```

A Non-Copy referent or subpart is never Moved through `ref`, `uniq`, `objref` or `objuniq` in a way that leaves the borrowed Place Moved or Uninitialized, even if a later reinitialization is planned; exclusive access does not transfer ownership. Copy acquisition leaves the source initialized and is not extraction.

A user-defined `deinit` assumes a complete value. A Partial Move that would invalidate this assumption, for the aggregate itself or for any enclosing ancestor, including through nested paths, methods and destruction, is rejected. A complete owned value whose own Type has a `deinit` may still be Moved as a whole.

When ordinary extraction is forbidden, use [exchange or swap](#157-whole-value-updates) or a Type-specific operation. Exchange preserves initialization; Type-specific invariants remain the implementation's responsibility and may require restricted storage access. A Field path never bypasses an intervening computed getter.

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

The declaration kind is structural, while accessibility depends on the use site. Constraints can prove structural facts but not current initialization or the absence of Loans. Unknown structural facts follow [generic Access Effect resolution](08-generics-constraints-and-contracts.md#89-generic-access-effects); there is no Consume contract syntax.

An ancestor may be incomplete if the target itself remains Initialized and complete and can be located without whole-value access to that ancestor; whole-receiver reads and borrows remain forbidden. A user-defined `deinit` can make a path structurally ineligible, so the actual ancestors are also checked at each use. Moving a complete value as a whole is distinct from a Partial Move.

Per-path state, destruction responsibility, first initialization of `let`, construction completion and current completeness are tracked across branches, loops, transfers and `defer`, and [destruction lifetime checks](#1566-destruction-lifetime-checking) apply. Raw-pointer operations need not recover or repair an untracked original owner's responsibility.

Lowering may elide transfers and temporary storage, or use conditional cleanup flags, only while preserving values, abstract Place identity and lifetime, Move state, Loans, Origins, destruction responsibility and specified failures. Optimization must not change which programs or Move Paths are legal.

### 15.1.5. Movable places

A **Movable Place** permits transferring ownership or capability out of its current value. The safe direct sources are owned root Places, Tuple elements, fixed-array elements at eligible constant indices, and authorized stored Property slots. A Move requires an Initialized, complete target, no conflicting Loan, accessible consuming operations, and valid construction, Partial Move, Origin and `deinit` conditions.

Borrowed referents, object fields, static storage, unsupported indices and hidden Property storage cannot supply safe extraction. Generic owned arguments may be Moved as whole values without an extra Contract. Raw dereference keeps its Unsafe obligations.

**Lending rule.** A spelling is required exactly where a directly owned Place is first lent exclusively (`@uniq`, `@objuniq`) or given away (`@move`); reborrowing through an existing exclusive reference and transferring a Temporary Value need none. Bare acquisition Copies a Copy Type and never Moves. `@move` transfers from a Movable Place even when its Type is Copy: it marks the source Moved and transfers its complete Type, dependencies and destruction responsibility; transferring a reference transfers capability, not ownership of the referent. A bare borrow value is reborrowed in the mode its position requires, never stronger than its own mode or its path's authority, and a Place reached through an exclusive reference is lent exclusively where a `uniq` is required (§10.2). A `let` binding may supply a transfer but cannot be reinitialized, and restoring a `var` needs write permission.

```kimi
let number: i32 = 10
let copied = number       // Copy; number remains initialized.
let resource = makeResource()
let taken = resource@move // Transfer; resource is now Moved.
// let bad = resource     // Error: a bare Non-Copy Place never Moves.
```

Getter results are acquired as results, never by moving hidden storage. An already owned temporary transfers to its destination under §3.6; borrowing the destination does not restore the original source.

### 15.1.6. Match acquisition and lifetime

`match E` evaluates `E` once and initializes an internal **Subject Place** under the **subject rule**, which `for` shares for its iterable (§14.6.2):

| Subject `E` | Acquisition |
| --- | --- |
| Owned Place, including a Copy one | Shared borrow; the Subject holds a `ref` |
| Borrow value | Copy of a shared reference, or shared Reborrow of an exclusive one |
| Temporary Value, including `x@move` and a call result | Whole-value acquisition by value, materialized without an extra acquisition |

This happens before arm selection, regardless of bindings, Wildcards, or whether any arm succeeds, and optimization cannot change the original Place's Move state, lifetime or Loans. A proven-Copy Place may be implemented by a Copy of its value instead of a shared borrow: the bindings of a Copy Subject are copies, so the two are observationally equivalent and no Loan is required. A custom, computed or required `get` subject invokes its getter once; a standard stored `get` uses the permitted Place operation. A borrowed Subject's Loan follows the uses of its bindings: an arm without a live borrowed binding may assign to or transfer the original Place.

```kimi
// Message is Non-Copy.
match message                       // Shared borrow.
    .Write(let text) => inspect(text) // text: ref/string
    _ => ()
use(message)                        // Valid after required Loans end.

match state                         // Arms without borrowed bindings may replace the Place.
    .Idle => state = .Running
    _ => ()

match message@move                  // Owned Subject: payloads Move.
    .Write(let text) => store(text@move)
    _ => ()
// use(message)                     // Error: the whole value was transferred.
```

Once an arm is selected, its body locals are initialized left to right from the [Candidate Places](14-control-flow.md#1483-guards). An unguarded arm is selected immediately on Pattern success; a guarded arm additionally requires successful guard cleanup.

| Access to the Binding position | Acquisition |
| --- | --- |
| The Subject itself, or an owned Tuple or payload path that traverses no borrow | Ordinary Copy/Move of the stored complete Type |
| An element reached by structural matching through `ref/T` | Shared reading under the [shared element-read rules](04-arrays-indexing-and-slices.md#466-slice-operations-and-element-results), without calling a getter |
| A Wildcard position | No acquisition; the existing responsibility stays with the Subject or the referent's owner |

Dependent read Types and Copy/Move/Borrow/Reborrow effects are resolved by the [generic Access Effect deadline](08-generics-constraints-and-contracts.md#89-generic-access-effects). Borrowed access cannot Move a Non-Copy referent or grant exclusive authority. In `.Some(let r)`, a stored `ref/T` is copied as a reference, and a nested Case Pattern through that reference restricts descendant acquisitions to shared access. New borrows require valid referents and owners; copied references keep their existing Origins and Loans.

For `wrapper: Option<Result<i32, i32>>`, matching `wrapper@ref` with `.Some(let r)` Copies the `Result` into `r`: borrowing the subject does not force payload bindings to be borrows. A change in payload Copy capability can therefore change binding Types and generic-body validity. There is no explicit borrow-binding or ordinary payload projection syntax; an API that needs such a borrow may accept the whole enum by reference instead, which may change its public Signature. [Pattern acquisition selection](appendices/D-deferred-features.md#d1-enum-and-pattern-extensions) remains a design boundary.

For owned decomposition, each selected Case identity and positional payload index is tracked as a Move Path inside the Subject, including recursive decomposition, together with the remaining initialization and destruction responsibility after each acquisition. These internal paths never apply to the caller's original enum and invent no ordinary payload projection syntax.

**Lifetime.** The Subject lasts for the match evaluation. A match result or outward transfer value is secured first; then the arm scope is cleaned up under ordinary Scope Exit, and finally the Subject's remaining initialized parts are destroyed. Body bindings enter scope left to right and are destroyed in reverse order, before the Subject; body locals and `defer` keep their normal reverse registration order. A Wildcard does not destroy immediately, and unselected arms acquire no body ownership. Match is exhaustive and has no unmatched normal-completion path. Each later step requires the earlier cleanup to complete normally; Abort does not unwind.

Borrowing through a borrowed Subject depends on the referent and the original Loan, not on the temporary slot storing that reference, and such a borrow may be returned when its original contract allows. A new borrow into an owned Subject's payload depends on the Subject and cannot escape the match. Copying or moving a stored external reference keeps that reference's external Origin instead. Returning a reference never extends its lifetime, and destroying a stored non-owning reference never destroys its referent. Guards have [stricter escape restrictions for new candidate-dependent Loans](14-control-flow.md#1483-guards).

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

The same projection applies to Origin-bearing local values, under ordinary lexical visibility; executable uses require definite initialization. A bare value name with no outer safe-borrow Origin is invalid as an Origin, even if its owned outer Type has borrowed contents; select a declared Origin with `x.source` instead. Thus `{r}` for a local `r: ref/T` always refers to `r`'s referent. Borrowing `r`'s slot uses an explicit layered Borrow (§13.5.5), whose slot Origin is inferred. Storage Origins of owned locals likewise remain compiler-internal and are inferred from initialization. Local values cannot supply Origins in public signatures or outside their lexical scope.

### 15.2.2. Ordering and intersection

`origin o1 outlives o2` requires `region(o1) ⊇ region(o2)`. The relation is reflexive and transitive, and `static` outlives every Origin. `origin o1 == o2` requires equal regions; it does not merge Loans or their referents.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

The intersection therefore contains no region outside either operand, and it may equal an operand. A result declared `{x and y}` is valid only in the region common to both inputs.

Intersections are normalized using these laws, where outlives simplification requires a proof under the [limited Origin solver](#1536-limited-origin-inference):

```text
a and b         = b and a
(a and b) and c = a and (b and c)
a and a         = a
a outlives b implies a and b = b
```

For a fixed binding and proof environment, intersections are flattened; proven-equal or mutually outliving Origins are replaced by the least stable representative; duplicates and operands proven to outlive another operand are removed; and the remainder is sorted. The sort uses a deterministic total order based on stable Binding Identity, including declaration binder and parameter position where applicable, not spelling, input traversal order or memory address. A singleton is its operand.

Intersections are renormalized after substitution or a change of proof evidence, and cached reductions must validate their proof dependencies. This is a canonical form under the permitted rules, not a general semantic-equivalence test. Input Loan dependencies are kept separately: simplifying an Origin expression never removes a distinct input's Loan.

### 15.2.3. `static` and `Owned`

```kimi
func empty() -> ref/string during static
```

A shared borrow from `static` has no non-static lifetime dependency and must satisfy the [static-source rules](11-properties.md#1132-static-storage). A new safe borrow of mutable static storage has a finite Origin. Safe code cannot derive `uniq/T during static` from longevity alone, because an exclusive borrow also requires a unique Loan anchor; an abstract Origin whose Loan requirement is `uniq` cannot be bound to `static` in safe code.

`static` describes an Origin. `Owned` expresses independence from non-static lifetime dependencies; it is neither ownership Semantics nor permission to allocate storage:

```kimi
func register<F>(f: F)
    F is Owned
```

A valid complete Type `T` is `Owned` exactly when every Origin in **OwnedOrigins(T)** equals `static`; an empty set satisfies the condition. OwnedOrigins is the conservative dependency closure of the Type's outer Origin; its Semantics target (value referent, object payload or View Target, or raw-pointer pointee Type); all instantiated Type and Origin arguments, including unused slots; bases; stored Fields; enum payloads; Tuple components; array elements; and concrete Closure captures. Aliases are expanded and declaration bindings substituted before traversal. Recursive Types use the structural fixed-point rules, not circular conformance evidence. A base or runtime-Contract view contributes its visible Type and Origin arguments; its hidden payload was certified at erasure (§15.8.1).

The OwnedOrigins of a nested Type include inherited explicit Origins and unused outer Type arguments (§6.1.3), so an empty `Outer<ref/i32 during local>.Tag` is not Owned. These Type-level dependencies imply neither a retained outer instance nor an actual Loan. Static storage uses the shared key of §22.2.4 without erasing full-reference lifetime checks.

Callable Types contribute every fixed Origin in their complete Type: a Function Item's bound generic and Origin arguments, a concrete Closure's captures and fixed signature Origins, and the fixed Origins written in a common Function Type's parameter and result Types. Only Origins bound per call, such as the direct-input quantification of §8.6 and §15.4, are excluded, because they have no fixed binding to prove. A common Function Type's hidden environment is certified Owned at erasure. An Owned proof never infers or rewrites a callable's per-call contract.

Established outlives facts are used: `a outlives static` proves `a` equal to `static`, since `static` is the maximum Origin. An unbound or unproven abstract Origin yields Unknown, not a proof of `not Owned`; required evidence is resolved by the ordinary deadline. A generic definition proves Owned for its own Type parameters and abstract Origins only from its declared Constraints and bounds (§8.10); that proof cannot wait for instantiation. Empty containers and unselected Cases do not weaken this Type-level check. In particular, `unsafe/(ref/i32 during local)`, and a wrapper with that non-static Type argument, cannot prove Owned even if no safe-borrow Field is visible. Traversing a pointee Type neither dereferences a pointer nor creates a Loan; unsafe implementations must still expose their actual lifetime dependencies and uphold pointer validity.

This revision requires Owned for static storage, concrete payload erasure into base or runtime-Contract views, common Function Type environment erasure, cyclic-factory payloads (§13.5.8), and explicitly declared Owned Constraints. A lifetime-hiding library API states that requirement explicitly; the compiler does not infer an "indefinite retention" capability from a private body. Ordinary storage and concrete object allocation impose no blanket Owned requirement. Owned never discharges acquisition, Loan, destruction-order, unsafe or concurrency checks.

## 15.3. Origin schemas, names and relations

The Origin system expresses lifetime dependencies using four concepts: an **Origin**, a **binding set** mapping a Type's schema slots to Origins, a **projection** selecting a slot, and a **relation** between Origins. Infer when possible; relate when necessary; name only when useful; declare when needed. These rules remove function Origin parameter lists and Type Origin mappings, not the semantic binders needed for universal contracts.

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

Borrow annotations use postfix `during` with the attachment and order of §3.3.6. Only `ref`, `uniq`, `objref`, `objuniq`, and a Semantics parameter proven to be a safe borrow accept them. `owner`, `obj`, `rc`, `arc` and `unsafe` do not. Whole-Type parentheses do not accept an annotation from outside.

The argument is one Origin atom: a simple name, `value.slot`/`set.slot`, `static`, or a parenthesized Origin expression. An intersection after `during` must be parenthesized: `during (a and b)`. Parenthesized single atoms are valid; empty parentheses, lists, trailing commas, `_`, calls and arbitrary value expressions are not. In `f(x: ref/T during a,)`, the comma belongs to the parameter list.

An outer `and` belongs to the surrounding grammar: `T is ref/U during a and Copy` is a requirement conjunction. In a Type-only position, `ref/T during a and b` is invalid, never reparsed by lookup. Relation clauses retain unrestricted Origin expressions on either side of `outlives` or `==`; these operators and the clause end delimit the operands. An annotation neither creates Loans nor extends lifetimes or performs acquisition, conversion or Reborrow.

A named Type reference's `{name}` introduces one binding-set name, with an optional trailing comma. It is never application of an existing Origin or set. The Type must have a nonempty schema known at definition; unknown generic schemas, duplicate names and use of a set as a scalar Origin are errors. Name each required occurrence separately and relate its slots. There is no whole-set equality, positional Origin application, mapping such as `{source => x}`, or call-site `f{a}(...)` application. `_` is neither a binder nor an inference request. Empty braces are permitted only on Type declaration headers (§15.3.2).

```kimi
func identity<T>(value: View<T>) -> View<T>{result}
    origin result.source == value.source
    return value
```

Naming preserves the Type, dependencies and quantification. Adding or removing a valid unused set name, or consistently renaming it and its references, leaves the contract unchanged. Naming an already complete Type does not reopen its bindings. Prefer a value projection when it directly names the needed slot; set names remain available for results, nested occurrences and other Type expressions.

Parse the brace role from syntactic position, independent of whitespace or lookup success: declaration context selects a Type schema header; a named Type suffix names a set. A following `/` does not turn a set into a borrow annotation. Reject brace borrow annotations before `/`, old Origin lists on functions, constructors and accessors, `origin`/`from` borrow annotations, and wrong-role brace contents.

**Projection.** A direct safe-borrow value name denotes its outer borrow Origin (§15.2.1). `value.source` selects a declared slot from the value's Type. Normalize aliases and redundant owner prefixes, then peel only consecutive safe-borrow layers to find that schema. Do not search through Fields, Type arguments or raw pointers. A set projection selects the corresponding slot of its named occurrence. Unknown slots are errors. Projection reads compile-time Type information; it invokes neither dereference nor getters and grants no initialization or Loan permission.

### 15.3.2. Type schemas and storage

Structs and enums can declare their own Origin slots in a header after generic parameters and before the base clause. Headers list simple fresh Names, allow a trailing comma, and contain no bounds, `static`, projections or intersections. Duplicate and inherited-name redeclarations are errors. Groups and Contracts declare no own slots and retain their enclosing environment.

| Header | Own slots |
| --- | --- |
| Absent | At most one distinct scalar Origin is inferred from directly written storage borrow annotations. |
| `{source}` or `{left, right}` | Exactly the listed slots; no implicit additions. |
| `{}` | No own slots; no implicit additions. Inherited and complete-Type dependencies remain. |

A written header **closes the schema**. Without one, collect simple Origin names from the whole selected storage schema: instance Fields, enum payloads and bases, including borrow annotations explicitly written inside Type arguments. Repeated occurrences of one name are one candidate; two different candidates reject the whole Type, independently of traversal order. Do not count `static`, set names, inherited dependency metadata or dependencies already bound inside a complete Type argument. Do not cross nested declarations or callable boundaries. This limit does not restrict a function's scalar or anonymous input Origins.

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

In headerless storage, a simple name is an own-slot candidate; collision with a visible inherited Origin is an error, not implicit capture. A Type directly referencing inherited Origins in storage requires a closed header, using `{}` if it adds no slots. In a closed Type, references resolve to its declared slots or the visible enclosing Origins.

```kimi
struct Outer<T> {source}
    struct Inner {}
        let value: ref/T during source
```

Every fragment of a split struct repeats the same closed header, including `{}` for zero own slots. Slot count, order and names agree under §6.1.2. Type relations occupy the same unique Constraint definition region as other Type Constraints; other fragments share it. This adds no Origin header to split groups.

**Storage completion.** Complete every stored borrow and aggregate slot from public Origins, `static`, complete Type arguments and explicit annotations/relations. Initializers and constructor assignments satisfy this contract; they never infer it. Do not synthesize hidden free slots or flatten a nested schema into the containing Type.

```kimi
struct Wrapper<T> {source}
    let value: View<T>{inner}
        origin inner.source == source
```

`inner` is local to that Field and its attached clauses. Any remaining Field condition must follow from the containing Type's public premises and intrinsic well-formedness. Additional public premises belong explicitly in the Type's Constraint region; private Field clauses cannot silently add them. Apply the same rule to every enum payload, regardless of the selected Case.

A complete Type has an established contract, including its Origin bindings and quantification; Origins need not be concrete regions. A schema includes lexically inherited slots but does not flatten the dependencies of Type arguments, Fields or bases. Those remain in their complete Types and in OwnedOrigins (§15.2.3). An empty schema alone does not prove Owned.

### 15.3.3. Declaration-attached relations

```kimi
origin a == b
origin a outlives b
```

Each clause contains one relation; multiple clauses are conjunctive. Both operands are Origin expressions (§15.2). `origin` and `outlives` are contextual here. Relations introduce no names. Chained comparisons, disjunction, negation and runtime tests are not added.

A clause is indented once under its declaration. Functions and Types place it in their leading Constraint region. Fields, locals, enum Cases, associated-Type specifications and Container aliases attach clauses to the declaration, before accessors or other bodies where applicable. Syntactic attachment determines the owner; referenced names must be visible there. Do not move clauses to the innermost referenced declaration or permit arbitrary expression constraint blocks.

| Owner | Obligation |
| --- | --- |
| Public function or Type contract | Publish premises and check the definition for every admitted binding; prove substituted premises at each use. |
| Field, payload or associated-Type specification | Bind the selected Type and prove remaining conditions from the enclosing public contract. |
| Base Type | Bind the header occurrence; publish additional premises explicitly in the Type's Constraint region. |
| Local or fixed Container alias | Check against established evidence and the initializer, where present; assume no new facts. |

Closed conditions independent of declaration parameters are definition-time proof obligations. A false condition cannot make an invalid definition vacuously acceptable. Type-intrinsic well-formedness remains separate from arbitrary Field preconditions and is preserved even for anonymous slots.

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

Search lexical scopes from inside out and stop at the first scope with an Origin-context candidate. A same-scope conflict among value, Origin and set roles is an error. A wrong-role match neither falls back outward nor becomes a fresh implicit name. Bare Fields are not value-Origin candidates without a receiver. Type-parameter names retain their separate namespace.

New Origin/set names cannot hide visible Origins, sets or competing parameter/local names. Duplicate set declarations are errors; mutually invisible Field-local names may be reused. Ordinary value-to-value shadowing is unchanged. Collect signature and schema names before resolution, independent of input, Field or file enumeration order; do not hoist Field-local sets or change executable visibility/capture boundaries. Recheck member bindings when an enclosing schema changes.

**Nested signatures.** Function Types, Callable and anonymous functions introduce no new named scalar Origins. Retain their limited per-call direct-input quantification and their own result omission rules. A set name inside a Function Type/Callable belongs to the declaration containing that Type expression. Each nested aggregate input slot must be fixed by a complete Type or an expression over existing outer Origins; an upper bound alone cannot leave a free per-call aggregate slot.

```kimi
func useView<T>(x: View<T>, callback: (View<T>{c}) -> ())
    origin c.source == x.source
```

`c.source` is fixed by the outer call, not selected again for each callback invocation. Callable permits this fixed set naming but gains no written direct-borrow Origin annotations. Nested results first retain fixed bindings, then use their own signature's result elision: `(ref/T) -> View<T>` can inherit the inner per-call input. An unused set label does not change that contract. Inner per-call Origins cannot be projected or captured outside their binder; slots referenced by outer clauses must instead be completed with fixed outer Origin expressions. Apply this boundary at every nesting level. Anonymous whole-result inference, expected Types and captures keep their existing rules.

### 15.3.5. Variance, Loan requirements and Phantom Origins

Origin relations are not value conversions. Preserve the complete-Type variance rules: `ref/T during o` is covariant in `o` and `T`; `uniq/T during o` is covariant in `o` and invariant in `T`; function parameters reverse polarity and results preserve it. Mutable storage follows its representation's invariance requirements. Infer declaration variance from all occurrences and solve recursive Types to a fixed point; no explicit variance annotations are added. These rules add no ordinary inheritance upcast or callable value operation.

Slots retain inferred Loan requirements `none < ref < uniq`: shared borrow use requires `ref`, exclusive use requires `uniq`, and multiple/nested uses propagate the stronger requirement. The requirement identifies necessary caller-side Loan retention; it neither grants a Loan nor changes structural Copy classification. Keep actual Place, authority, anchor and Reborrow identities across acquisition, storage, calls, results and destruction. Equal or shortened Origins never merge distinct Loans or manufacture exclusive access.

```kimi
struct RawView<T> {source}
    let pointer: unsafe/T
    let count: isize
    func get(self: ref/Self, index: isize) -> ref/T during self.source
```

A header slot without a corresponding safe stored reference is a **Phantom Origin**. Its dependency is retained, but its declaration grants no pointer validity, Loan, Copy or access authority. Unsafe implementations or verified intrinsics must establish initialization, bounds, alignment, permissions and retention. Required input-derived Loans remain attached to dependent values. Treat a general phantom slot as invariant when safe shortening cannot be established structurally; verified intrinsic Types retain their established metadata. No general phantom authority inference is introduced. Static Origins still obey §15.2.3's source and unique-anchor restrictions.

### 15.3.6. Limited Origin inference

Place temporary inference variables only at unresolved positions; fixed Origins never become variables again. Collect constraints recursively using complete-Type variance, explicit relations and use requirements.

| Position | Principal candidate |
| --- | --- |
| Covariant | Unless fixed by equality, the meet of all upper bounds: their longest common region. |
| Invariant | Proven equality; never replace invariant inner Origins with a meet. |
| Contravariant | A lower bound proven to outlive every other lower bound. Do not invent unions of incomparable bounds. |
| Mixed or cyclic | A representable, unique principal solution modulo proven Origin equivalence. |

A principal solution is the most general permitted solution under variance and fitting. Multiple shorter regions do not make a covariant meet ambiguous. Substitute each candidate into every condition, including internal dependencies and Loans. If no principal solution is expressible, or only incomparable candidates remain, require an explicit annotation.

Use equality substitution, reflexivity, transitivity and meet laws. Preserve composite expression nodes: `a and b outlives c` decomposes into two requirements, whereas `x outlives a and b` does not decompose into atomic edges or a disjunction. Identical normalized premises and consequences of the permitted rules are usable. Do not enumerate arbitrary regions or require general theorem proving. Unknown proofs remain distinct from contradictions; unresolved obligations are errors at the existing finalization deadline unless §8.10 explicitly permits deferral.

### 15.3.7. Canonical contracts and verification

Retain normalized complete Types, binders and scopes, fixed bindings, activation conditions, relations, intrinsic well-formedness and Loan requirements/dependencies. For example, `ref/View<T>{v} during borrow` requires `v.source outlives borrow`. Intrinsic conditions are definition premises and use-site obligations, not access permissions.

Schema slots have stable declaration-bound identities; distinct declarations with the same spelling remain distinct. Identify anonymous inputs by declaration, input position, normalized Type occurrence and target slot. Grouping and redundant owner prefixes create no new slots. Recursive Types establish finite schemas before computing dependency/variance fixed points; never discover infinitely expanded anonymous slots during instantiation.

Contract equality compares corresponding normalized structure, quantification, conditions, bindings and guarantees. Origin/set spelling or explicit versus implicit declaration alone distinguishes neither overloads nor specializations. Public Type slot names are still API: adding, renaming or changing their relations affects projection clients even when their source Fields are private.

Compatibility keeps the owning feature's Type-structure rules, admits every call allowed by the requirement and provides at least its result guarantees. Treat required universal Origins as rigid arbitrary symbols; only implementation-side instantiable call Origins may be solved. Fixed Types/captures stay fixed. Check input `required <: implementation` and result `implementation <: required`, then prove implementation conditions from requirement premises, never from the obligations themselves. Check receivers, environments, authority and Loans separately, for every admitted Semantics condition. Specializations and stored accessors inherit their original complete contracts. Preserve contracts and proof dependencies through function references, artifacts and reload.

Share normalized schemas separately from occurrence bindings. Reuse interned structures, stable IDs and scratch buffers; invalidate caches when bindings, premises, activation conditions or proof dependencies change. Closed headers establish slot names/count/identity early, not variance, Loan requirements, Copy or layout independently of storage. Union-find and strongly connected components can help atomic relations, but composite conditions and fitting are not mere graph reachability. Do not mandate an always-materialized transitive closure, which may require quadratic space. Use-site Loan, initialization and access checks remain necessary. Origins add no runtime arguments or lifetime tags and do not alone duplicate generated code (§21.3).

## 15.4. Origin completion and elision

Complete contracts at definition: collect declarations and occurrences; inherit complete Types; resolve names/projections; normalize annotations and explicit relations; complete remaining omitted positions; retain the result for body verification and use-site inference. Naming a binding set alone does not suppress omission. Result annotations do not infer a named function's contract from its body.

### 15.4.1. Explicit result relations

For an ordinary callable's aggregate result slots:

1. Keep established complete/inherited bindings and positions determined by an explicit annotation or equality substitution to an existing Origin expression.
2. Universally quantify still-unbound result slots remaining in nontrivial outlives relations after normalization of the **explicit `origin` clauses**, subject to those relations. Upper bounds, lower bounds and composite relations are all permitted.
3. Apply result elision (§15.4.3) to the rest. Equalities connecting only unresolved result slots form one unresolved equivalence class; all positions' defaults must agree.

Intrinsic Type well-formedness alone does not turn an omitted result slot into a new universal binder. Check it against the completed contract. Normalize equality and mutual outlives while retaining any established binding; preserve nonsubstitutable equalities as both outlives directions and never infinitely expand cyclic expressions. Tautologies such as `static outlives x`, `x outlives x` and `x == x` do not change omission or quantification. Do not prove a condition trivial by assuming that same condition; normalization is deterministic and independent of clause order, without promising general semantic-equivalence testing.

```kimi
func shorten<T>(value: ref/T) -> View<T>{r}
    origin value outlives r.source

struct Marker {source}
func makeMarker() -> Marker{r}
    origin static outlives r.source // Redundant; source defaults to static.
```

A directly annotated result-only scalar name, such as `ref/T during s`, remains an explicit universal contract. No dedicated syntax universally quantifies an otherwise unconstrained aggregate result slot. Locals, storage and nested signatures follow their own completion rules. Completion occurs once: substitution may renormalize expressions but never rerun elision or change binders when a condition becomes trivial. A relation on a complete Type checks its binding rather than rebinding it.

### 15.4.2. Position rules

Normalize aliases, grouping and redundant owner prefixes first. These rules apply to aggregate slots and safe-borrow layers; `unsafe/T` gains no borrow Origin.

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

Ordinary input completion recurses through Type arguments, Tuple/array elements and borrow targets, not through Fields, already complete Types or another callable boundary. Distinct inputs, occurrences and slots remain independent; nested borrow layers gain no new omission permission. Constructors keep the containing Type's result contract; parameters do not automatically correspond to Fields. Original pair WholeTypes `s/T` preserve their bindings; reconstructed `s/U` retains conditional safe-borrow slots under §8.1.2. Function Types, Callable and anonymous functions keep §15.3.4's boundaries.

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

Evaluate conditional direct-borrow inputs under every admitted Semantics condition. Do not add an implicit Owned constraint or weaken fixed dependencies to make elision succeed. An Origin-compatible result still requires valid Type formation and actual Loan authority.

For `func f<T>(x: ref/T? during a) -> ref/T`, x is an Option rather than a direct borrowed input, so the shared result defaults to `static`. Dependence on a requires an explicit result `during a`. If this default causes a result fitting/lifetime error, explain the omitted-input boundary and suggest visible explicit Origins without choosing among multiple candidates. A function correctly returning `static` needs no warning.

### 15.4.4. Locals and independent Type expressions

Local inference permits ordinary shortening and preserves variance, outlives and Loan obligations. The declaration fixes its Type and inference variables; later uses constrain them without reopening inference, and later assignments must satisfy that same contract. An initializer is required for genuinely omitted positions. Non-generic omissions resolve before body Origin/Loan analysis completes; permitted generic obligations resolve by their established deadline. Uncertainty never becomes invented `static`, a fresh universal Origin or erased dependencies.

```kimi
let view: View<T> = makeView(value)
    origin view.source == value

var pending: View<T>{p}
    origin p.source == value
```

Collect the attached clauses together with the Type annotation before checking initialization. In its own attached clause, `view.source` refers only to the declaration's Type slot, not an initialized value. Initializer lookup retains ordinary visibility; `let x = x` still refers to an outer `x`. Without an initializer, equalities or established proofs must determine every slot as an existing Origin expression; an upper bound alone or a later first assignment is insufficient. Definite initialization is unchanged.

Bind independent Type expressions by attaching relations to their containing declaration:

```kimi
struct Derived<T> {source}: Base<T>{b}
    origin b.source == source

associate Source.Element is View<T>{e}
    origin e.source == source

alias StaticHelpers => Family.Helpers{h}
    origin h.source == static

let result = consume<View<T>{argument}>(view)
    origin argument.source == view.source
```

These examples assume the appropriate existing open base, associated-Type requirement and inherited group schema. Preserve their non-Origin access, role, construction and lookup rules. Aliases remain Container aliases, not general or generic Type aliases, and cannot reference runtime values or another file's bindings.

Set names in source-written initializer Type expressions, including explicit Type arguments, construction qualifiers and Adaptation Targets, belong to the local declaration. Do not enter nested functions/declarations or discover names inside inferred Types. Apply §15.3.4 to nested callable Type expressions. Expressions without attached clauses can use ordinary contextual inference; use an intermediate declaration when extra relations are needed. Leading function contract clauses cannot name arbitrary body Type occurrences. Keep `(Outer<T>{o}).Inner<U>` for intermediate Container qualifiers and validate even dependencies absent from the final Type.

Ordinary storage preserves complete Types and actual Loan identities through acquisition, Move/Copy, calls and destruction. Arrays, Dictionaries, Tuples, fixed arrays, structs, enums, concrete object payloads and Closure environments gain no blanket Owned restriction. Heap placement does not extend a referent's lifetime. Loan liveness follows required uses and observable destruction (§15.6), not merely lexical scope.

## 15.5. Exclusive origins

An exclusive borrow requires both a valid Origin and a unique Loan anchor: an Origin proves longevity but not uniqueness.

The following independent member-signature excerpts are declared inside `View<T> {source}`; bodies are omitted to focus on Origin and Loan contracts. A shared borrow may be returned from a stored Origin:

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

One valid form consumes the Origin-bearing owner, so that moving `self` prevents reuse of the capability:

```kimi
struct View<T> {source}
    func into_uniq(self: Self)
        -> uniq/T during self.source
```

Alternatively, the result reborrows through the current exclusive receiver; the parent Loan stays active, and access through it is suspended, while the returned reborrow is live:

```kimi
struct View<T> {source}
    func get_uniq(self: uniq/Self)
        -> uniq/T during self
```

## 15.6. Borrow checking

Function bodies are lowered to a control-flow graph. A **program point** is a position immediately before or after an operation. A [Place](03-types-and-values.md#34-values-places-and-storage) does not by itself grant write permission. The lowered representation uses these projections:

```text
place := local
       | place '.' FieldIdentity
       | place '.base' BaseIdentity
       | place '.' TupleIndex
       | '*' place
       | place '[' _ ']'
```

These projections describe direct Field and lowered storage Places. Base and Field identities preserve inherited paths; no ordinary base-reference conversion is implied. Custom, computed and required accessors use function boundaries instead (Chapter 11). Parentheses preserve Places. A read Copies, Moves or borrows according to context and permissions.

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
x@uniq.mutate()  // Allowed: r is no longer live.
```

### 15.6.1. Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `a : b` requires `region(a) ⊇ region(b)`.                    |
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

Inline parts exclude pointer and reference referents. Distinct shared-reference or raw-pointer variables alone do not prove independence. Constant fixed-array indices use only the [ConstantIndexExpression rule](#1513-move-paths-and-partial-move), comparing decoded in-range literal values, not general constant evaluation or optimization; runtime index comparisons such as `i != j` establish no disjointness, and no arbitrary integer proof or optimizer result changes acceptance. Array-derived Slices keep the whole-array Loan footprint through reslicing, splitting and empty views under the [Slice lifetime rules](04-arrays-indexing-and-slices.md#465-slice-storage-lifetime-and-permissions). Simultaneous exclusive borrows may be used only through their valid access paths, and reborrowing still suspends conflicting parent access.

These are storage rules, not permission to bypass Property accessors. Direct Field operations may borrow disjoint fields separately, while custom, computed and required calls keep their receiver footprint.

Each operation is checked against every active Loan on an overlapping Place:

| Operation               | Existing `ref` | Existing `uniq` |
| ----------------------- | -------------- | --------------- |
| Read                    | Allowed        | Forbidden       |
| Write or Move           | Forbidden      | Forbidden       |
| Create `ref`            | Allowed        | Forbidden       |
| Create `uniq`           | Forbidden      | Forbidden       |
| Destroy the borrowed Place | Forbidden   | Forbidden       |

This enforces shared aliasing or mutation, never both at once.

### 15.6.3. Reborrowing

Borrowing through an exclusive borrow creates a child Loan anchored on the referent Place, with a region contained in the parent's Origin. While the child is live, the parent Loan remains live but access through the parent reference is suspended; overlapping access is rejected by the normal conflict rules. The child does not depend on the lifetime of the variable that holds the parent reference, so a Reborrow of a parameter may be returned or stored like a copied reference.

```kimi
func bump(n: uniq/i32)

var v = 0
bump(v@uniq)
bump(v@uniq)
```

Each call creates a temporary reborrow; the first ends before the second starts.

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
func make(a: ref/A, b: ref/B)
    -> Pair<A, B>{
        left => a,
        right => b}
```

While the returned `Pair` is live, shared Loans on both `a` and `b` remain active. A dependency requiring `uniq` propagates an exclusive Loan. The spelling `static` alone creates no parameter-root Loan, but it does not erase the storage anchors and conflicts of a borrow into static Field storage.

Receiver and argument protection begins at Borrow/Reborrow formation in evaluation order. Eligible exclusive operations start with the call reservation of §15.6.7; other Loans are immediately active. After activation, call protection lasts through the entire call, including callee cleanup, not merely the callee's last use, and extends for dependent results. Intrinsics and collection methods use the same rules. These checks supply the call-wide attribute proof of §21.5.5.

**Static call effects.** Each callable's potentially accessed static Field identities, and its read, shared or exclusive borrow, write, replacement and destruction effects, are summarized, including those of callees, defaults, lazy initialization and cleanup. They are compared with active caller Loans by the normal overlap rules. Borrowed results keep Field anchors and dependency paths: borrows of immutable sources may be `static`, whereas borrows of mutable sources keep a finite Origin under §11.3.2. Static allocation never permits replacing or destroying a borrowed current value.

Summaries distinguish first-access initialization effects from ordinary accesses. A live Loan anchored to a Field proves that the Field has completed initialization, so its initializer need not be counted again; this proves nothing about an unrelated Field first accessed by the callee. If a result may derive from several static Fields, every possible anchor is kept conservatively, independently of the runtime branch taken.

For example, if `let view = State.text@ref` borrows a mutable static string Field, `view` has a finite Origin, and `State.reset()` is rejected while `view` has a later use if `reset` may replace that Field; a shared Loan still permits read-only calls. The whole call is summarized conservatively, and favorable runtime branches need no special analysis. Recursive fixed points are computed before acceptance. Separately compiled and indirect calls consume published validated summaries, or conservatively treat unknown effects as conflicting with every potentially affected active static Loan; clients need not inspect private bodies. Immutable anchors are kept for shutdown dependencies even after erasure, while borrows of mutable sources cannot cross an Owned boundary. FFI validity and aliasing obligations still apply.

These static and capture anchors are kept when composing [receiver-preservation effects](12-expressions.md#12442-effect-verification), even when `self` is not an explicit argument; the same call may affect multiple roots. Published summaries and their dependencies follow §18.3 and §21.3.4.

### 15.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

```kimi
func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    return local@ref // Error: the local cannot satisfy the universal Origin x.
```

The local value cannot satisfy the universal return Origin `x`, so returning its borrow is a compile-time error.

### 15.6.6. Destruction lifetime checking

The [destruction rules](16-scope-exit-and-destruction.md#163-aggregate-destruction-and-deinit) and [Scope Exit](16-scope-exit-and-destruction.md#162-scope-exit-destruction) determine responsibility and order. Destruction lifetime checking applies to every Origin and Loan that destruction may observe, and requires validity at each such observation:

```text
DestructorUsePoints(value, origin) ⊆ region(origin)
```

Destruction that observes no Origin or Loan adds no lifetime requirement. Every user-defined `deinit` is conservatively assumed to observe all reachable Origins, even if its body does not use them, and the same checking applies recursively to field destruction. No relaxation mechanism is defined.

```kimi
struct Logger {sink}
    let out: uniq/Writer during sink

    deinit
        observe(self.out)
```

Here `observe` accepts `ref/Writer`, and reading `self.out` shares the stored capability instead of extracting it. `sink` must remain valid during destruction, even if the `deinit` body were replaced with `()`.

### 15.6.7. Call borrow reservations

A **call reservation** delays exclusive access, not evaluation or lifetime protection. It belongs to one invocation's preparation and is not a value, Type, Semantics or Origin. No runtime lock, allocation or fallible activation is required.

**Eligibility.** A final exclusive Borrow/Reborrow that directly prepares a receiver or parameter starts a reservation of its target. This includes explicit `@uniq`/`@uniq/T` and `@objuniq`, the implicit exclusive Reborrow of a borrow value or of a Place reached through an exclusive reference (§10.2), permitted exclusive object projections, and generic adaptations with that resolved effect. Parentheses are transparent. A lending point followed by standard field or element projections that call no user code, up to the invocation, is one preparation: `holder@uniq.items.append(holder.items.length)` reads its argument during reservation and equals `holder.items@uniq.append(...)`. A getter or another invocation on that path activates at its own call. Direct, indirect, Callable, generic, constructor and intrinsic invocations follow the same rule. Resolve the operation and overload first; reservation legality never changes candidate ranking or retries selection.

The directly written adaptation and its call-only Reborrow form one preparation chain. They must not first activate an intermediate exclusive Loan. Other operand operations keep their own evaluation and Loans. Reservations do not pass through local or aggregate storage, Closure captures, nested invocations, or results of `if`, `match`, `do` or other control-flow expressions. Such expressions use ordinary Borrow/Reborrow rules internally; a subsequent call adaptation cannot demote their already active Loans. An inner invocation activates its own reservations before entry. Ordinary exclusive borrows outside eligible preparation remain immediately active. Compound assignment and indexing keep their own evaluation rules; lowering them to helper calls grants no new reservation permission.

**Reservation.** Evaluate and locate the target once, checking initialization, declared access, mutability, complete-Type/Origin validity and the existing exclusive authority. Protect its Place and the owner/provenance needed to keep that location valid. Shared reads and shared Borrows through otherwise valid paths may coexist. Conflicting writes, Move, destruction, reallocation and independent exclusive operations or reservations are forbidden. Structural non-overlap follows §15.6.2; equal Origins do not establish equal Loans or disjointness. A reservation is not a shared borrow that can manufacture exclusive authority.

An exclusive child reservation preserves its parent's authority and dependencies. It permits temporary shared inspection through an authorized parent path, but never re-enables independent access blocked by an already active ancestor Loan. Activation suspends conflicting parent access under normal Reborrow rules. Object projection, Property permissions and ObjectCallCompatible requirements remain unchanged.

**Preparation and activation.** Locate/adapt the receiver first for a method call, evaluate explicit arguments in textual order, then omitted defaults in parameter order (§7.2–3). Defaults may temporarily inspect prepared reserved inputs through shared access under §7.2; they cannot use them as active exclusive references or retain a new inspection Loan. End those temporary inspection Loans before entry. No other temporary is destroyed early merely to make activation possible.

After all inputs are prepared, verify that every reservation can be active simultaneously. Check the complete argument set, retained and returned dependencies, captures, static anchors and cleanup effects. A shared Loan may coexist during reservation only if it does not conflict at activation. Owned or Copy results can still retain Loans. Overlapping exclusive arguments and shared/exclusive argument pairs are rejected even if the callee would not use them. Activation is a static proof boundary before the logical callee entry, not an ordered sequence that permits transient aliasing. All existing call-wide and result-Loan rules then apply.

**Abandoned calls.** Transfers retain reservation protection while their operands are evaluated and acquired. End only reservations belonging to invocations abandoned by that transfer, without activating them, before the abandoned preparation's remaining cleanup. Preserve actual Loans retained by other values and all normal acquisition/cleanup ordering. A transfer caught inside an argument does not abandon the enclosing call. Divergence enters no callee; Abort keeps its no-cleanup contract. Unreachable syntax is still checked under the ordinary rules.

```kimi
var p: i32 = 1
Kimi.Intrinsics.replace(p@uniq, with: p + 1) // Read during reservation.
Kimi.Intrinsics.exchange(p@uniq, with: p + 1)
// Kimi.Intrinsics.swap(p@uniq, p@uniq)      // Error: overlapping targets.
```

Diagnostics distinguish reservation, activation and the conflicting use or retained Loan. Suggest a separate local only when its acquired value's dependencies allow the call.

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

Directly owned Places are lent with `@uniq`; existing value borrows and Places reached through exclusive references use ordinary Reborrow. Object payloads require an explicit projection at ordinary argument positions. Reference or handle *storage* is borrowed with a fully specified target. If `T` is a borrow Type, the operations transfer its reference value and capability, not ownership of its referent. Property access and hidden-storage permissions still apply.

These operations cannot repair Uninitialized, Moved or partially moved storage; `=` keeps its existing repair rules. They permit no incomplete MoveOut through a borrow, no unrestricted construction or destruction receivers, and no assignment to the immutable `self` binding. Whole-content replacement may replace values containing `let` fields without granting individual writes to those fields.

### 15.7.1. Evaluation and transfer

Argument evaluation, target reservation and activation follow §15.6.7, including textual order for named arguments. Acquisition and fitting finish before any update. If an argument does not complete, no update occurs; earlier effects remain, and the ordinary cleanup and Abort rules apply.

`replace` destroys the complete old value in its original location using its normal `deinit`/field/base order. Abort or divergence during that destruction prevents placement, and no rollback is promised. Placement transfers the preconstructed new value without rerunning constructors, initializers or setters. The transfers of `exchange` and `swap` execute no user code, destruction or Abort-producing operation. Their internal empty state is unobservable; no inter-thread atomicity is promised.

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

### 15.7.2. Static non-overlap

`swap` requires the structural proof of [Place overlap](#1562-place-overlap-and-conflicts). Independent roots, distinct inline fields, Tuple elements or constant fixed-array indices, and valid simultaneous exclusive borrows with distinct Loan anchors can prove disjointness; identical or containing Places overlap. Dereferences follow Loan provenance, and different shared references or raw pointers alone prove nothing. Unknown relationships are rejected, even when runtime comparisons or optimization suggest separation.

```kimi
func swapValues<T>(a: uniq/T, b: uniq/T)
    Kimi.Intrinsics.swap(a, b) // Valid live exclusive inputs establish distinct anchors.
```

### 15.7.3. Storage update dependencies

These rules apply to `=`, `replace`, `exchange` and `swap`. Preparation, acquisition, movement, destruction, placement and cleanup must preserve every Loan and Origin needed by incoming, returned or surviving values; moving contents can invalidate dependencies even when nothing is destroyed. The lifetime of storage is distinct from the lifetime of its current contents.

The operation's exclusive Loan and its parent chain are preserved. Updates that would invalidate live borrows anchored to the old contents are rejected, and placing a new value at the same address never restores revoked dependencies. A result that borrows data owned by the old destination cannot survive destruction of that data. Independent external dependencies may survive: copying a stored `ref/Data` may keep an external `Data` Loan, whereas borrowing that field's storage as `ref/(ref/Data)` depends on the field itself. Actual anchors are checked, not surface syntax.

Updating a complete object payload preserves the object's Identity, allocation, Dynamic Type, descriptor, header and ownership mode. Payload destruction is not final object release: counts are not altered, the enclosing object's lifecycle state does not change, and its storage is not freed. Nested owning handles still follow ordinary destruction. The special receiver, reentry, view and resurrection rules apply during content destruction, and an empty or destroying payload is never exposed through an ordinary view. Final release destroys the *current* payload once and frees the object. An exchanged old value carries a separate destruction obligation.

Facts and projections about the old contents, including `let` fields, are invalidated, while valid storage access and object Identity and Dynamic Type refinements are preserved (§14.10.1). Handle replacement follows its separate rules. Unsafe code and optimization obey the same dependency and allocation-lifetime distinction, and raw pointers cannot substitute for Loan evidence.

## 15.8. Captured and erased dependencies

### 15.8.1. Object payload erasure

Erasing a concrete payload behind a base or runtime-Contract view requires its complete data Type to satisfy `Owned` under the conservative OwnedOrigins closure of §15.2.3; the handle's own Origin is not part of this test. Payload Type and Origin arguments and the ordinary exclusive-Loan restrictions are resolved before erasure. A local object borrow may still have a local Origin when its payload is Owned.

```text
Dog owns only i32/string data -> Owned payload -> base/contract erasure allowed
Dog stores a local ref       -> non-Owned     -> initial erasure rejected
objref/Animal during local     -> borrow remains local even when payload is Owned
```

This is not a blanket `{static}` requirement on object handles or exact concrete views; same-target operations keep the existing lifetime rules. An erased view certifies that the check succeeded, and later upcasts and casts inherit that certification without runtime Origin queries. Only proof-covered fixed Origin bindings may be supplied as `static` in a checked cast (§13.6.2); per-call callable Origins and the handle's outer Origin are not reconstructed. Existing borrowed-field Types remain valid; hiding their non-static dependencies needs a later existential-view design. Owned does not waive pointer validity, Loan, destruction or concurrency checks.

### 15.8.2. Closure dependencies and call results

A Closure recursively keeps every captured value's Origin and Loan dependencies, not merely the lifetime of its creation Block. Moving an owned value with no borrowed contents does not borrow its old local storage. Copying a shared reference, moving an exclusive reference, reborrowing, or acquiring a borrowed aggregate preserves the corresponding external Origins, child Loans and parent restrictions. Independent dependencies are never collapsed or discarded at generic substitution or Type erasure. OwnedOrigins (§15.2.3) applies to the captured Types when the environment is proven Owned.

A Closure's captured dependencies are distinct from each call's receiver and result dependencies:

| Result source | Contract |
| --- | --- |
| Borrow of environment-owned data | Depends on the current Closure receiver borrow |
| Copy of a captured external shared reference | Keeps its external Origin |
| Reborrow of a captured exclusive reference | Also depends on the current exclusive call Loan |
| External reference value moved out by a Consuming call | Transfers that reference's external Origin and capability |
| Borrowed argument | Follows the input/result Origin contract |

When the whole result Type is omitted and inferred from the body, its Origin and Loan dependencies are inferred too. Explicit result annotations and fixed expected signatures keep the ordinary result-Origin elision: the hidden environment receiver is not a new source-level `self` or directly written borrow parameter.

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

Multiple results are joined under normal result validation, keeping all possible Loan dependencies when an Origin meet is taken. Invariant mismatches, cycles and unexpressible dependencies are rejected rather than weakened. A repeatable exclusive result keeps its call Loan until the needed uses end, preventing conflicting reentry. No result may outlive call-local storage or borrow an environment consumed by that call; moving an external reference value out is distinct and may be valid.

The internal call contract is preserved for concrete generic use, and callers do not inspect bodies to rediscover lifetimes. Common Function Types and Callable constraints may return input-dependent borrows but cannot expose results that depend on the hidden receiver (§8.6).

### 15.8.3. Escape and retention

Escape describes retention across a creation or call boundary, not a permanent syntactic Closure category. Returned values, saved fields, other Closures and indirect callees are checked against the destination's lifetime and capability contracts. Borrow capture is not inherently non-escaping, and Move capture does not remove nested borrow dependencies. Moving or heap-allocating a Closure cannot extend a local referent's lifetime, and temporaries keep their original expiration.

Non-escaping means that the callee retains neither the callable nor its environment dependencies beyond the call; it does not mean a single call, no allocation or waived Loan checks. `ref/F` and an Owned result let a verified callback-only body use a borrowed concrete environment without erasure, but neither `ref/F` nor Callable alone promises that no environment-derived value can be saved. A separately compiled callee cannot be assumed non-escaping without a verified contract. Non-escaping declaration syntax and borrowed erased callable views remain deferred.

Loan validity covers later uses, results and dependencies observed by destruction, not just the last body invocation or the lexical end of a binding. Concrete Closure storage follows §15.4, and the Owned boundaries are listed in §15.2.3. The Owned guarantee grants no thread-transfer or concurrent-access capability; see the [concurrency design boundary](appendices/D-deferred-features.md#d2-concurrency-memory-model-and-thread-transfer).

## 15.9. Lifetime design boundaries

This revision does not define:

- abstract Origin parameters owned by Contracts or trait-like abstractions; static Contracts may inherit outer Origins under §6.1.3, and the [Property getter receiver and result contracts](11-properties.md#1122-computed-properties) introduce no Contract-level Origin parameters;
- existential object views that hide non-static payload dependencies;
- general higher-ranked Origins beyond the direct-input quantification of Callable constraints;
- Origin expressions naming static Places, such as a function result bounded by a mutable static Field; use direct access, an input-bounded result (§11.3.2), a scoped callback or immutable static storage instead;
- lending iterators;
- cancellation cleanup guarantees.

These features require extensions of the [ownership and Origin rules](#15-ownership-and-lifetime-analysis) and must not be inferred from this revision.
