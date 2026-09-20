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
let name = pair.0  // Partial Move; pair is incomplete.
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

Ordinary acquisition Copies Copy Types and otherwise Moves. A Move marks the source Moved and transfers its complete Type, dependencies and destruction responsibility; moving a reference transfers capability, not ownership of the referent. A `let` binding may supply a Move but cannot be reinitialized, and restoring a `var` needs write permission. There is no forced Move of Copy Types.

```kimi
let number: i32 = 10
let copied = number // Copy; number remains initialized.
let resource = makeResource()
let taken = resource // Non-Copy Move; resource is now Moved.
```

Getter results are acquired as results, never by moving hidden storage. An already owned temporary transfers to its destination under §3.6; borrowing the destination does not restore the original source.

### 15.1.6. Match acquisition and lifetime

`match E` evaluates `E` once and initializes an internal **Subject Place** by ordinary whole-value acquisition: Copy for a Copy Type, Move otherwise. An existing Temporary Value is materialized without an extra acquisition. This happens before arm selection, regardless of bindings, Wildcards, or whether any arm succeeds, and optimization cannot change the original Place's Move state, lifetime or Loans. A custom, computed or required `get` subject invokes its getter once; a standard stored `get` uses the permitted Place acquisition.

```kimi
// Message is Non-Copy.
match message
    _ => ()
// use(message)                    // Error: the whole value was Moved.

match anotherMessage@ref
    .Write(let text) => inspect(text) // text: ref/string
    _ => ()
use(anotherMessage)                // Valid after required Loans end.
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
| Abstract | `source`, `left`        | Origin parameter declared by a function or Type |
| Static   | `static`                | Built-in maximum Origin                         |

```text
origin-expression := Name
                   | origin-expression '.' Name
                   | static
                   | origin-expression 'and' origin-expression
```

A direct safe-borrow parameter, receiver or local used as an Origin denotes its value's outer borrow Origin, never the lifetime of the variable's storage:

```kimi
func first(x: ref/T) -> ref{x}/T
```

`x.source` denotes the abstract Origin `source` carried by `x`. Qualification is required so that values of the same Origin-bearing Type remain distinguishable:

```kimi
struct View<T> {source}
    func get(self: ref/Self) -> ref{self.source}/T
```

The same projection applies to Origin-bearing local values, under ordinary lexical visibility; executable uses require definite initialization. A bare value name with no outer safe-borrow Origin is invalid as an Origin, even if its owned outer Type has borrowed contents; select a declared Origin with `x.source` instead. Thus `{r}` for a local `r: ref/T` always refers to `r`'s referent. Borrowing `r`'s slot uses an explicit layered Borrow (§13.5.5), whose slot Origin is inferred. Storage Origins of owned locals likewise remain compiler-internal and are inferred from initialization. Local values cannot supply Origins in public signatures or outside their lexical scope.

### 15.2.2. Ordering and intersection

`o1 : o2` means that `o1` outlives `o2`: `region(o1) ⊇ region(o2)`. The relation is reflexive and transitive, and `static` outlives every Origin.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

The intersection therefore contains no region outside either operand, and it may equal an operand. A result declared `{x and y}` is valid only in the region common to both inputs.

Intersections are normalized using these laws, where outlives simplification requires a proof under the [limited Origin solver](#1534-generic-origin-inference):

```text
a and b         = b and a
(a and b) and c = a and (b and c)
a and a         = a
a : b implies a and b = b
```

For a fixed binding and proof environment, intersections are flattened; proven-equal or mutually outliving Origins are replaced by the least stable representative; duplicates and operands proven to outlive another operand are removed; and the remainder is sorted. The sort uses a deterministic total order based on stable Binding Identity, including declaration binder and parameter position where applicable, not spelling, input traversal order or memory address. A singleton is its operand.

Intersections are renormalized after substitution or a change of proof evidence, and cached reductions must validate their proof dependencies. This is a canonical form under the permitted rules, not a general semantic-equivalence test. Input Loan dependencies are kept separately: simplifying an Origin expression never removes a distinct input's Loan.

### 15.2.3. `static` and `Owned`

```kimi
func empty() -> ref{static}/string
```

A shared borrow from `static` has no non-static lifetime dependency and must satisfy the [static-source rules](11-properties.md#1132-static-storage). A new safe borrow of mutable static storage has a finite Origin. Safe code cannot derive `uniq{static}/T` from longevity alone, because an exclusive borrow also requires a unique Loan anchor; an abstract Origin whose Loan requirement is `uniq` cannot be bound to `static` in safe code.

`static` describes an Origin. `Owned` expresses independence from non-static lifetime dependencies; it is neither ownership Semantics nor permission to allocate storage:

```kimi
func register<F>(f: F)
    F is Owned
```

A valid complete Type `T` is `Owned` exactly when every Origin in **OwnedOrigins(T)** equals `static`; an empty set satisfies the condition. OwnedOrigins is the conservative dependency closure of the Type's outer Origin; its Semantics target (value referent, object payload or View Target, or raw-pointer pointee Type); all instantiated Type and Origin arguments, including unused slots; bases; stored Fields; enum payloads; Tuple components; array elements; and concrete Closure captures. Aliases are expanded and declaration bindings substituted before traversal. Recursive Types use the structural fixed-point rules, not circular conformance evidence. A base or runtime-Contract view contributes its visible Type and Origin arguments; its hidden payload was certified at erasure (§15.8.1).

The OwnedOrigins of a nested Type include inherited explicit Origins and unused outer Type arguments (§6.1.3), so an empty `Outer<ref{local}/i32>.Tag` is not Owned. These Type-level dependencies imply neither a retained outer instance nor an actual Loan. Static storage uses the shared key of §22.2.4 without erasing full-reference lifetime checks.

Callable Types contribute every fixed Origin in their complete Type: a Function Item's bound generic and Origin arguments, a concrete Closure's captures and fixed signature Origins, and the fixed Origins written in a common Function Type's parameter and result Types. Only Origins bound per call, such as the direct-input quantification of §8.6 and §15.4, are excluded, because they have no fixed binding to prove. A common Function Type's hidden environment is certified Owned at erasure. An Owned proof never infers or rewrites a callable's per-call contract.

Established outlives facts are used: `a : static` proves `a` equal to `static`, since `static` is the maximum Origin. An unbound or unproven abstract Origin yields Unknown, not a proof of `not Owned`; required evidence is resolved by the ordinary deadline. A generic definition proves Owned for its own Type parameters and abstract Origins only from its declared Constraints and bounds (§8.10); that proof cannot wait for instantiation. Empty containers and unselected Cases do not weaken this Type-level check. In particular, `unsafe/(ref{local}/i32)`, and a wrapper with that non-static Type argument, cannot prove Owned even if no safe-borrow Field is visible. Traversing a pointee Type neither dereferences a pointer nor creates a Loan; unsafe implementations must still expose their actual lifetime dependencies and uphold pointer validity.

This revision requires Owned for static storage, concrete payload erasure into base or runtime-Contract views, common Function Type environment erasure, cyclic-factory payloads (§13.5.8), and explicitly declared Owned Constraints. A lifetime-hiding library API states that requirement explicitly; the compiler does not infer an "indefinite retention" capability from a private body. Ordinary storage and concrete object allocation impose no blanket Owned requirement. Owned never discharges acquisition, Loan, destruction-order, unsafe or concurrency checks.

## 15.3. Abstract origins

Ordinary functions, constructors, explicit accessor signatures, Contract function/accessor requirements, structs and enums may declare abstract Origin parameters. Order is Name, generic `<...>`, Origin `{...}`, then value parameters or base clause. Contracts themselves, groups, specializations, Function Types, Callable signatures, anonymous functions and standard accessors have no own explicit Origin list.

Every Origin brace list is nonempty and permits a trailing comma. `{}` is invalid; omit an unnecessary list. Declarations contain simple Names with optional bounds; `static`, projections, intersections and named mappings are not declaration names. There is one list per declaration; duplicate and inherited names are errors. The whole list is visible in the base, inputs, result, constraints and body. `_` introduces neither an anonymous binder nor an inference request. Whitespace does not change attachment; conventional forms are `f<T> {a}(...)`, `View<T>{a}` and `ref{a}/T`.

```kimi
func unwrap<T> {s}(v: View<T>{source => s})
    -> ref{s}/T

struct View<T> {source}
    let value: ref{source}/T
```

Function Origins are universally quantified. Origin parameters occupy a namespace distinct from Type parameters.

An Origin parameter may have one bound, `name : target`, meaning that `name` outlives `target`. The target is `static` or an abstract Origin visible in the declaration, including any binder in the same list; the whole list is bound before bounds are resolved. A bound is not a default Origin argument. Duplicate binders and unknown targets are errors. Reflexive bounds are redundant, and cycles require equal regions under §15.2.2.

```kimi
struct Holder<T> {stored}
    var value: ref{stored}/T

func store<T> {a, b : a}(
    holder: uniq/(Holder<T>{stored => a}),
    value: ref{b}/T)
    holder.value = value
```

Here `b : a` permits shortening the stored reference to `a`. The holder receiver's outer Origin is independently inferred as an input Origin and does not replace the stored Origin `a`.

Bounds are part of the declaration contract of functions and Origin-bearing Types, including enums and Contract method requirements. Bodies are checked assuming the declared bounds, and the substituted bounds are proven at every call or Type use with the limited solver (§15.3.4). An Unknown proof is an error at the ordinary finalization deadline, and the body cannot infer additional caller requirements. A Type's members and constructors inherit its bounds. Bounds grant no Loan, initialization or access permission.

Bounds distinguish neither overloads nor specialization keys. Contract implementations must admit every Origin binding allowed by the requirement, and specializations inherit the original bounds. Container fragments repeat the same bounds (§6.1.2). Bound identities and proof dependencies are kept in artifacts, and affected uses are invalidated when they change. There is no standalone outlives clause, no written bound on an implicit input Origin (Type well-formedness relations still apply), and no bound declaration inside a Function Type or Callable signature; use explicit function Origin parameters to relate inputs.

### 15.3.1. Origin arguments

Named Origin arguments use `{...}` and `=>`:

```kimi
struct Pair<A, B> {left, right}
    let a: ref{left}/A
    let b: ref{right}/B

Pair<A, B>{
    left => a,
    right => b}
```

A reference's trailing `{...}` binds its declaration schema, never its enclosing borrow. A single expression is shorthand only when exactly one effective slot remains unbound; otherwise use names, as in `{left => a, right => b}`. A one-slot named mapping is also allowed. `{a, b}` is not positional application or an intersection; use `{a and b}` for one intersection expression. Do not mix expressions with named mappings. Unknown, duplicate and already-bound slots are errors. Partial named lists apply the position rule independently to each missing slot (§15.4).

The declaration schema, bounds and existing bindings must be determinable during generic definition checking. Unknown-schema `T{a}` is an error; a later concrete Type cannot reinterpret it as a borrow annotation. Fully bound Type parameters and `Self` retain their complete dependencies. Effective schemas and intermediate qualifiers follow §9.6.1.1.

Borrow annotations instead precede `/`: `uniq{borrow}/Writer<T>{target}`. Only `ref`, `uniq`, `objref`, `objuniq`, or a Semantics parameter proven to be a safe borrow accept this annotation. `owner`, `obj`, `rc`, `arc` and `unsafe` do not. A borrow annotation contains one Origin expression and optional trailing comma, not a mapping or bound. Semantics prefixes associate to the right; annotations on parenthesized whole Types are forbidden (§3.3.6).

Parse braces once and validate their role. After a Semantics keyword require `/` and a target. After a bare Name, a following `/` selects the Semantics role; otherwise the braces are reference arguments. Declaration context selects declaration syntax. Failed lookup never changes the chosen role. Diagnose empty lists, mixed forms and wrong-role items at their source. Old `origin`/`from` syntax is not accepted, but these words may be ordinary identifiers. There is no function-call Origin application `f{a}(...)`.

### 15.3.2. Variance

These are static subtype rules under [Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations). They neither create nor authorize a value operation; acquisition and existing Loan obligations are checked separately.

The compiler infers Origin variance from all occurrences and solves recursive Types to a fixed point; explicit variance annotations are not allowed.

| Position                 | Origin                   | Core         |
| ------------------------ | ------------------------ | ----------------- |
| `ref{o}/T`           | Covariant in `o`         | Covariant in `T`  |
| `uniq{o}/T`          | Covariant in `o`         | Invariant in `T`  |
| Function parameter       | Reverses polarity        | Contravariant     |
| Function result          | Preserves polarity       | Covariant         |
| Interior-mutable storage | Representation-dependent | Usually invariant |

For an Origin parameter `p` of `S`:

- covariance permits `S{p => o1} <: S{p => o2}` when `o1 : o2`;
- contravariance reverses that relation;
- invariance requires equal Origins.

The direct borrow rules are:

```text
o1 : o2
--------------------------------
ref{o1}/T <: ref{o2}/T
uniq{o1}/T <: uniq{o2}/T
```

`uniq/T` remains invariant in `T`. These variance rules add no ordinary inheritance upcasts and no value operations for callable signature matching; see the separately defined [object adaptations](13-operators-and-assignment.md#1357-object-upcasts).

### 15.3.3. Loan requirements

Each abstract Origin has an inferred Loan requirement:

```text
none < ref < uniq
```

Using an Origin in `ref/T` requires `ref`, and using it in `uniq/T` requires `uniq`. Multiple uses take the stronger requirement, and requirements propagate through nested Origin-bearing Types.

```kimi
struct View<T> {source}
    let value: ref{source}/T       // loan(source) = ref

struct MutView<T> {source}
    let value: uniq{source}/T      // loan(source) = uniq
```

The requirement determines which caller-side Loan must stay active while a returned or stored Origin-bearing value is live. It is not an additional Copy classification condition: [Copy capability](03-types-and-values.md#351-copy-capability-and-explicit-duplication) is structural, while actual Loan conflicts are checked at each use.

### 15.3.4. Generic Origin inference

For both ordinary and pair slots, an inference variable is placed at each unresolved Origin position in the complete Type `W`. Variance applies recursively to nested Types and aggregate mappings. An explicitly fixed Origin never becomes an inference variable again.

| Position | Constraints and candidate solution |
| --- | --- |
| Covariant | Inputs `a` and `b` contribute `a : alpha` and `b : alpha`. Without an equality constraint, the meet of all upper bounds is used: their longest common region. |
| Invariant | Proven Origin equality is required; invariant inner positions of `uniq/V` are never replaced with a meet. |
| Contravariant | The constraint direction is reversed. If one lower bound provably outlives every other lower bound, that least upper bound is chosen; no union of incomparable bounds is invented. |
| Mixed or cyclic | Equality and ordering constraints are combined; a representable, unique principal solution modulo proven Origin equivalence is required. |

A **principal solution** is the most general permitted solution under the Type's variance and fitting relation. Multiple shorter solutions do not make a covariant inference ambiguous: the longest common region is chosen. Inference is rejected, requiring an explicit annotation, when no principal solution is expressible or only incomparable candidates remain.

The initial solver uses equality substitution, reflexivity and transitivity of established outlives facts, and the common-lower-bound laws of `and`. It uses the forced equality for invariant positions, the meet of upper bounds for covariant positions, and the comparable-lower-bound rule for contravariant positions. Candidates are substituted into every constraint, including inner dependencies and use requirements. Arbitrary regions are never enumerated, and no general theorem proving is used. An unresolved dependency may be kept only under the [deferred-obligation rules](08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations).

```text
choose<s/T>(x: s/T, y: s/T) -> s/T
Inputs:      ref{a}/i32, ref{b}/i32
Constraints: a : alpha, b : alpha
Binding:     W = ref{a and b}/i32
Result:      W, retaining both input Loan dependencies
```

This example has no additional constraints. Changing the input order does not change the solution, and an expected result cannot extend `a` or `b`. Even equivalent Origin regions keep separate input Loans. Exclusive use still requires a unique Loan anchor and a valid acquisition or Reborrow after Origin inference succeeds.

### 15.3.5. Canonical Origin contracts

At definition, retain one normalized contract for input/result Types and the reference environment: explicit and anonymous binders with scopes and activation conditions; fixed Type/capture bindings; Origin expressions and projections; declared bounds; Type well-formedness relations; and result Loan requirements. Inference variables are temporary solutions, not public binders. Distinguish Origin equality from Loan identity.

For example, `uniq{borrow}/Writer<T>{target}` requires `target : borrow` and preserves `T`'s own dependencies. Type-intrinsic relations are definition premises and call-site proof obligations even for anonymous Origins; bodies cannot invent additional caller conditions. Origin annotation or simplification is not a value conversion and grants no lifetime or exclusive capability.

Identify anonymous slots by declaration, input position, normalized Type occurrence and target slot. Grouping and redundant owner prefixes do not create slots; distinct occurrences stay independent. Resolve projections to those slots. After substitution or premise changes, simplify activation conditions and exclude inactive slots from comparison, retaining inner dependencies. Construction history creates no Type-identity difference.

**Equality and compatibility.** Equality compares corresponding normalized structure, quantification, conditions, bounds and result dependencies. Explicit versus anonymous spelling, names or explicit arity alone do not distinguish otherwise equal callable contracts. Declaration-fragment and Type-schema correspondence still apply. Compatibility instead admits every call allowed by the requirement and provides at least its result guarantees:

1. Apply the owning feature's structure rules: Contract input structure is equal; common Function Types and Callable keep their ordinary variance.
2. Treat required quantified Origins as rigid arbitrary symbols. Only implementation-side instantiable call Origins become inference variables; captures and fixed Type bindings remain fixed.
3. Collect input `required <: implementation` and result `implementation <: required` constraints. Solve for implementation variables using §15.3.4; Contract results may use a subtype already allowed by the Type rules.
4. Prove implementation bounds, well-formedness and guarantees from requirement premises, never from the obligations themselves. Check Loans, receiver, environment and access separately.

Apply this procedure under each admitted Semantics condition for conditional reconstructed slots (§8.1.2). Inactive slots need no principal solution. Definition proofs cover all admitted cases; instantiation cannot rescue a missing proof. No general conditional theorem solver, new higher-ranked quantification or erasure of dependencies is introduced.

A universally quantified implementation may be instantiated to a fixed required Origin. The reverse cannot restrict arbitrary required inputs to `static`. Preserve complete contracts on function references; ordinary calls infer their bindings. Origin spelling, count, bounds or explicitness alone create neither overloads nor specializations.

**Implementation guidance.** Share normalized schema/contract structure while separating occurrence bindings. Lazy slot references and interned structural IDs can avoid allocations. Cache keys include bindings, premises, activation conditions and dependency versions; validate collisions and invalidate changed evidence. Reuse OwnedOrigins analyses. ID comparison may be constant-time within one ID domain, but construction, hashing whole contracts and dependency updates are not promised constant-time. Never skip use-site Loan, initialization or access checks. After verification, existing generation rules erase Origins without duplicating machine code solely for Origin differences (§21.3).

## 15.4. Origin elision and return contracts

Origin omission depends on the position of the complete Type. These rules apply to aggregate Origin slots and the borrow layers `ref`, `uniq`, `objref` and `objuniq`, after alias expansion and normalization of grouping and redundant `owner` prefixes. They create no safe-borrow Origin for `unsafe/T`.

| Type position | Meaning of an omitted Origin |
| --- | --- |
| Direct borrowed parameter or borrowed receiver | An independent input Origin for that input's outer borrow layer. |
| Aggregate slot in an ordinary callable input | Inherit an established contract first; otherwise an independent universal Origin, recursively through nested aggregate Types. |
| Function result | The result-Origin rules below; anonymous whole-result inference keeps its own rules. A named function without a result annotation returns Unit. |
| Local binding with an inferred Type | The Type, Origin dependencies and Loans are inferred from the initializer under the ordinary acquisition rules. |
| Explicit local Type containing a borrow or Origin-bearing aggregate | Omitted Origins and Origin arguments are inferred from the declaration initializer and the ordinary Origin/Loan constraints; without an initializer, omission is a compile-time error. |
| Instance Field | Explicit bindings are required for all borrow layers and required Type Origin arguments; the storage contract is never inferred from initialization. |
| Static Field | Owned and the static-source rules of §11.3.2 are required. Omitted shared borrow Origins, and aggregate Origin arguments with Loan requirement `none` or `ref`, default to `static`; an exclusive borrow layer or a `uniq` Loan requirement is rejected. Bound Type and Origin arguments and callable contracts are preserved. |
| Generic Type argument or nested value Type | The enclosing position's rule applies recursively; being a Type argument introduces no separate default. |
| Enum Case payload declaration | The instance storage rule applies to every payload element, including nested Origins and required aggregate arguments; see [enum payloads](06-declarations-and-containers.md#63-enums). |
| Constructor parameter | The ordinary parameter rule; the constructed value keeps the containing Type's declared Origin contract under [construction](06-declarations-and-containers.md#623-constructors). |
| Property accessor | First inherit the corresponding complete stored contract, if any (§11.3). Otherwise complete actual inputs/results independently, including computed and required signatures. |
| Getter result, explicit or defaulted from the Property | Function result elision with the actual getter receiver, preserving already bound dependencies (§11.3). |
| Adaptation Target | Result Origins are inferred from the operand, operation and constraints under [Adaptation Targets](13-operators-and-assignment.md#1351-forms-and-adaptation-targets); this is not signature result elision. |
| Callable constraint signature | Its limited per-call direct-input quantification and result restrictions under [Callable constraints](08-generics-constraints-and-contracts.md#86-callable-constraints), rather than recursive quantification of every nested borrow. |

**Input Origins.** First inherit an already established complete contract: stored custom values/results from storage and specialization positions from the original declaration. Check explicit bindings without overwriting them. Otherwise, each missing aggregate Origin slot in an ordinary function, constructor or accessor input (including Contract requirements) introduces an independent universally quantified parameter supplied by the caller. Partially specified lists follow the same rule. Separate inputs, occurrences and slots receive separate binders, though callers may bind them to equal regions. Definitions must work for every binding satisfying published bounds and well-formedness; no body-based or static default is used.

Recurse through Type arguments, Tuple/array elements and borrow targets, but do not expand fields or already bound complete Types, and do not cross another Function Type/Callable signature. Preserve the original `s/T` WholeType (§8.1); reconstructed `s/U` uses its conditional position rule. Direct borrowed inputs/receivers keep their independent outer Origins. Nested borrow layers gain no new omission permission.

```kimi
func inspect<T>(left: View<T>, right: View<T>) -> ()
// Equivalent signature when View has one Origin:
func inspect<T> {a, b}(left: View<T>{a}, right: View<T>{b}) -> ()

func nested<T>(items: Array<View<T>>) -> () // Independent inner aggregate slot.
func layers<T> {a}(value: ref/ref{a}/T) -> () // Inner borrow is explicit.
```

Function Types, Callable and anonymous functions keep their own existing quantification/expected-Type rules. A missing aggregate input slot there must come from a complete bound Type/contract or be explicit where syntax allows it. `(View<T>) -> ()` does not introduce quantification; `(View<T>{a}) -> ()` has a fixed binding. Callable forbids written Origin annotations and must receive such dependencies through complete bound Types. Each nested signature still applies its own result rules.

Constructor inputs do not correspond automatically to fields; prove storage/result compatibility without inferring a precondition backward from an assignment. Instance storage and enum payloads still require explicit Origins; local initialization is inference, not universal quantification. Contract/alias/standalone Container references must supply unresolved slots.

**Local inference.** For locals, inference means satisfying the ordinary subtyping, variance, outlives and Loan constraints, not literal equality with the initializer's Origin; permitted shortening remains available. The declaration fixes the local Type and its Origin constraints; later assignments must satisfy that contract and cannot extend a source lifetime or erase a retained Loan dependency. Explicit Origin annotations remain constraints on the initializer and all subsequent assignments.

Local Origin omission requires an initializer, but inference may still fail. Each omission is bound at the declaration to a fixed inference variable with initializer-derived constraints, and a later assignment cannot supply a missing annotation. Later uses constrain those variables without reopening Type inference. Region and Loan constraints may remain symbolic during analysis, and generic obligations may await permitted substitution or instantiation.

Non-generic omissions are resolved before body Origin/Loan analysis completes, and generic obligations before instantiation is finalized. Failure to determine or validate the contract by its deadline is a compile-time error requiring an annotation or corrected constraints. Equivalent valid region solutions need not identify one unique point set. Uncertainty is never replaced with `static`, an invented abstract Origin or erased dependencies; `static` is a default only where elision explicitly allows it.

**Instance storage** exposes the containing Type's declared Origin contract, including dependencies already bound within complete generic Type arguments. Directly written borrow dependencies bind to declared abstract Origins, or explicitly to `static` where valid, and an exclusive borrow still needs a unique Loan anchor. Initializers and constructors satisfy this contract; they do not infer it.

```kimi
struct Box<T>
    let value: T
// Box<ref{a}/i32> retains a through its complete Type argument.
// No synthetic named Origin parameter is added to Box.
```

The constructed `Box`'s lifetime, acquisition and destruction keep that dependency. This does not permit a directly written field `value: ref/i32` to omit its Origin. Static storage can keep the `Box` when its complete Type proves Owned and its values satisfy §11.3.2. Finite layout, Copy derivation and object payload erasure remain separate checks.

**Ordinary storage.** Array, Dictionary, Tuple, fixed array, struct, enum, concrete object payloads (`obj`/`rc`/`arc`) and concrete Closure environments accept valid complete stored Types without a blanket Owned or Storable requirement. Type and Origin arguments and value-level Loan identities, anchors and Reborrow relationships are preserved through acquisition, storage, Move/Copy, calls and destruction. Conservative OwnedOrigins checks include Type-level dependencies that create no actual Loan, while actual Loans still require provenance. Heap placement neither extends a referent's lifetime nor changes these rules. Loan liveness follows required uses and observable destruction (§15.6), not merely the enclosing lexical scope.

For example, `func singleton<T>(value: T) -> Array<T>` with the body `return [value]` is valid without Owned or Copy: it acquires `T` once and propagates its complete dependencies. The same applies to a body-local Array, even when no Array appears in the public signature. All admitted Types are verified at definition time under §8.10; representation obligations cannot hide new capability requirements. A copied shared-reference element keeps its original referent's lifetime, while a borrow of the element slot is also bounded by the Array's storage (§4.6.6).

```kimi
func f<T>(x: ref/T, y: ref/T)
// Independent input Origins x and y.

func nested<T> {inner}(x: ref/(ref{inner}/T))
// The outer borrow has implicit input Origin x; inner is explicit.

func invalid<T>(x: ref/ref/T) // Error: the inner Origin is omitted.

struct View<T> {source}
    var value: ref{source}/T

func use<T>(x: ref/T)
    var local: ref/T = x // Infer an Origin satisfying initialization and use constraints.
    var missing: ref/T  // Error: no initializer to infer the omitted Origin.

group Global
    let number: i32 = 1
    var counter: i32 = 0
    let value: ref/i32 = Global.number@ref    // Omitted Origin is static; immutable source.
    let invalid: ref/i32 = Global.counter@ref // Error: a mutable static source has a finite Origin.
```

**Result elision.** Inherit an already established complete result contract first. For each remaining omitted borrow-layer Origin or aggregate slot, including partial named lists:

1. Preserve explicit bindings and dependencies in complete Types; if nothing is omitted, do not complete anything.
2. If there are direct borrowed inputs, use the meet of their outer Origins. Never add aggregate-internal Origins as candidates.
3. Otherwise, shared borrow layers default to `static`; exclusive layers require an explicit valid Origin. An aggregate slot defaults to `static` only when all input Types are provably Owned from existing premises (vacuously true for no inputs), and its inferred Loan requirement is `none` or `ref`. Unknown or Refuted Owned evidence, or a `uniq` requirement, requires an explicit Origin.

Owned is exactly §15.2.3's predicate; do not add a hidden input constraint or invent a weaker dependency summary. Shared borrow defaults preserve existing behavior. Aggregate defaults avoid silently fixing a relationship intended for a newly anonymous input to `static`. Borrow results may still explicitly depend on aggregate inputs.

Conditional direct-borrow candidates participate under their activation conditions (§8.1.2). Determine the result plan at definition and validate every admitted case. Preserve variance, outlives, Loans and fixed dependencies; no result Origin is inferred from a named function's body. If a case cannot be completed, require an explicit contract.

```kimi
func describe<T>(value: T) -> ref/string // ref{static}/string
func identity<T>(value: View<T>) -> View<T> // Error: specify the result Origin.
func get<T>(value: View<T>) -> ref{value.source}/T => value.value
func firstView<T> {source}(items: ref/Array<View<T>{source}>) -> View<T>{source}
```

To expose an inner Origin, use a declaration name or an existing value projection. Type-argument paths gain no new projection operation; for an anonymous slot inside `Array<View<T>>`, name that slot explicitly in both input and result as above. Diagnostics identify the nested input occurrence and suggest this repair. Existing local projections remain valid only in their lexical scope, not in public signatures.

```kimi
func first(x: ref/T) -> ref/T
// result Origin: x

func choose(x: ref/T, y: ref/T) -> ref/T
// result Origin: x and y

func empty() -> ref/string
// result Origin: static

func view<T>(x: ref/T) -> View<T>
// View<T> declares Origin source: result is View<T>{source => x}.
// Its owned outer Type does not suppress the retained borrow dependency.

func pair<A, B>(x: ref/A, y: ref/B) -> Pair<A, B>{left => x}
// left remains x; omitted right becomes x and y.
```

Only direct borrowed parameters participate in rule 2; Origins nested in aggregate inputs must be selected explicitly:

```kimi
func get<T> {s}(v: View<T>{source => s}) -> ref{v.source}/T
```

An explicit Origin annotation overrides elision only for the borrow layer or named Origin arguments it binds; omitted bindings elsewhere still follow the rules above. Thus this result depends on `self`, not on the conservative meet `self and key`:

```kimi
func lookup(self: ref/Self, key: ref/Key)
    -> ref{self}/V
```

Whole-result inference for anonymous functions also infers environment-derived Origins and Loans under the [Closure result rules](#1582-closure-dependencies-and-call-results); an explicit annotation keeps the elision above.

### 15.4.1. Return contracts

A declared return Origin limits the dependency visible to callers without requiring a borrow from that specific input. Every explicit or implicit result, including unreachable ones, must subtype the declared result Type under [result validation](14-control-flow.md#149-result-validation) and [reachability](14-control-flow.md#1492-reachability).

For example, `ref{static}/T` may satisfy `ref{x}/T` because `static : x`, provided the Origin position is covariant. Invariant positions require equality, and contravariant positions reverse the subtype direction.

`{x and y}` is deliberately conservative in two ways:

- the result region is `region(x) ∩ region(y)`;
- Loans for both possible sources remain active while the result is live.

```kimi
let r = choose(a, b)
b.mutate()       // Error: the Loan on b is still active.
use(r)
```

The caller cannot rely on which argument the implementation actually selected. An Origin-bearing result Type with distinct Origin parameters can preserve more precision.

## 15.5. Exclusive origins

An exclusive borrow requires both a valid Origin and a unique Loan anchor: an Origin proves longevity but not uniqueness.

The following independent member-signature excerpts are declared inside `View<T> {source}`; bodies are omitted to focus on Origin and Loan contracts. A shared borrow may be returned from a stored Origin:

```kimi
struct View<T> {source}
    func get(self: ref/Self)
        -> ref{self.source}/T
```

Returning `uniq{self.source}/T` from `self: uniq/Self` is invalid, because detaching the result from the current `self` Loan could allow a second exclusive borrow:

```kimi
struct View<T> {source}
    func bad(self: uniq/Self)
        -> uniq{self.source}/T       // Error
```

One valid form consumes the Origin-bearing owner, so that moving `self` prevents reuse of the capability:

```kimi
struct View<T> {source}
    func into_uniq(self: Self)
        -> uniq{self.source}/T
```

Alternatively, the result reborrows through the current exclusive receiver; the parent Loan stays active, and access through it is suspended, while the returned reborrow is live:

```kimi
struct View<T> {source}
    func get_uniq(self: uniq/Self)
        -> uniq{self}/T
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
x.mutate()       // Allowed: r is no longer live.
```

### 15.6.1. Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `a : b` requires `region(a) ⊇ region(b)`.                    |
| Well-formedness | Every Origin in `T` observable through `ref{o}/T` or `uniq{o}/T` must outlive `o`. |
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

Borrowing through an exclusive borrow creates a child Loan. While the child is live, the parent remains live but access through it is suspended; overlapping access is rejected by the normal conflict rules.

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

A call's receiver and argument Loans begin as each borrow or reborrow is formed in evaluation order, before later arguments and defaults; in particular, an exclusive receiver is active while explicit arguments are evaluated. Receiver and argument borrow protection lasts through the entire call, not merely the callee's last use, and extends for dependent results. There is no two-phase reservation exception; intrinsic exchange/swap and dynamic collection mutation use the same rule. These language checks supply the common value-borrow attribute proof of §21.5.5.

**Static call effects.** Each callable's potentially accessed static Field identities, and its read, shared or exclusive borrow, write, replacement and destruction effects, are summarized, including those of callees, defaults, lazy initialization and cleanup. They are compared with active caller Loans by the normal overlap rules. Borrowed results keep Field anchors and dependency paths: borrows of immutable sources may be `static`, whereas borrows of mutable sources keep a finite Origin under §11.3.2. Static allocation never permits replacing or destroying a borrowed current value.

Summaries distinguish first-access initialization effects from ordinary accesses. A live Loan anchored to a Field proves that the Field has completed initialization, so its initializer need not be counted again; this proves nothing about an unrelated Field first accessed by the callee. If a result may derive from several static Fields, every possible anchor is kept conservatively, independently of the runtime branch taken.

For example, if `let view = State.text@ref` borrows a mutable static string Field, `view` has a finite Origin, and `State.reset()` is rejected while `view` has a later use if `reset` may replace that Field; a shared Loan still permits read-only calls. The whole call is summarized conservatively, and favorable runtime branches need no special analysis. Recursive fixed points are computed before acceptance. Separately compiled and indirect calls consume published validated summaries, or conservatively treat unknown effects as conflicting with every potentially affected active static Loan; clients need not inspect private bodies. Immutable anchors are kept for shutdown dependencies even after erasure, while borrows of mutable sources cannot cross an Owned boundary. FFI validity and aliasing obligations still apply.

These static and capture anchors are kept when composing [receiver-preservation effects](12-expressions.md#12442-effect-verification), even when `self` is not an explicit argument; the same call may affect multiple roots. Published summaries and their dependencies follow §18.3 and §21.3.4.

### 15.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

```kimi
func bad(x: ref/i32) -> ref{x}/i32
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
    let out: uniq{sink}/Writer

    deinit
        observe(self.out)
```

Here `observe` accepts `ref/Writer`, and reading `self.out` shares the stored capability instead of extracting it. `sink` must remain valid during destruction, even if the `deinit` body were replaced with `()`.

<a id="157-initialization-preserving-exchange"></a>

## 15.7. Whole-value updates

The following ordinary declarations belong to the public `Kimi.Intrinsics` group (§22.1.1) and have compiler-intrinsic implementations selected by declaration identity; a same-spelled user function has no intrinsic behavior. `T` is any valid complete value Type; these operations impose no `Sealed`, `Copy` or `Owned` constraint.

```kimi
public func replace<T>(target: uniq/T, with => value: T) -> ()
public func exchange<T>(target: uniq/T, with => value: T) -> T
public func swap<T>(first: uniq/T, second: uniq/T) -> ()
```

Each target must be fully Initialized, exclusively writable and permitted to undergo a whole-value update, and its complete Type must match the incoming value exactly, including generic arguments and internal Origins. Ordinary complete owner storage may contain an open Core. Object payload storage instead needs the complete-target proof of §13.5.5.1; neither a base subobject nor an open object View qualifies.

| Operation | Old contents | New contents | Result |
| --- | --- | --- | --- |
| `Kimi.Intrinsics.replace` | Destroyed at the original location | Installed only after destruction completes | Unit |
| `Kimi.Intrinsics.exchange` | Transferred without destruction | The acquired value is installed | The old value and its responsibilities |
| `Kimi.Intrinsics.swap` | Both transferred without destruction | Contents and responsibilities exchanged | Unit |

Owner Places use ordinary implicit exclusive borrowing, and existing value borrows use ordinary Reborrow. Object payloads require an explicit projection at ordinary argument positions. Reference or handle *storage* is borrowed with a fully specified target. If `T` is a borrow Type, the operations transfer its reference value and capability, not ownership of its referent. Property access and hidden-storage permissions still apply.

These operations cannot repair Uninitialized, Moved or partially moved storage; `=` keeps its existing repair rules. They permit no incomplete MoveOut through a borrow, no unrestricted construction or destruction receivers, and no assignment to the immutable `self` binding. Whole-content replacement may replace values containing `let` fields without granting individual writes to those fields.

### 15.7.1. Evaluation and transfer

Explicit arguments, including named arguments, are evaluated in textual order, and defaults under the ordinary call rules. Each target Loan begins at borrow formation and protects the target through the call, including during later arguments; there is no two-phase borrowing exception. Argument acquisition and fitting finish before any update. If an argument does not complete, no update occurs; earlier effects remain, and the ordinary cleanup and Abort rules apply.

`replace` destroys the complete old value in its original location using its normal `deinit`/field/base order. Abort or divergence during that destruction prevents placement, and no rollback is promised. Placement transfers the preconstructed new value without rerunning constructors, initializers or setters. The transfers of `exchange` and `swap` execute no user code, destruction or Abort-producing operation. Their internal empty state is unobservable; no inter-thread atomicity is promised.

```kimi
var p: i32 = 0
// Kimi.Intrinsics.replace(p, with: p + 1) // Error: target Loan conflicts with the later read.
let next = p + 1
Kimi.Intrinsics.replace(p, with: next)
let old = Kimi.Intrinsics.exchange(p, with: 5)
p = p + 1                     // Valid: assignment evaluates its RHS first.
var q: i32 = 9
Kimi.Intrinsics.swap(p, q)
// Kimi.Intrinsics.swap(p, p)             // Error: overlapping exclusive targets.
```

Diagnostics identify the borrow and the conflicting use. For a conflict with a later argument, they suggest precomputing that argument in a local only when its dependencies permit it. For a Non-Copy `x`, `x = x` can Move and reinitialize, whereas `Kimi.Intrinsics.exchange(x, with: x)` conflicts with the already active target Loan.

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
objref{local}/Animal     -> borrow remains local even when payload is Owned
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
let get = func [text] () => text@ref
// Internal signature: call(self: ref/Self) -> ref{self}/string.
let view = get()
let moved = get // Error if view is still used below.
inspectText(view)

let other = makeText()
let invalid = func [other] () -> ref/string => other@ref
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
