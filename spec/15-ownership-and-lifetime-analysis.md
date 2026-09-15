# 15. Ownership and lifetime analysis

[Specification index](../SPEC.md)

## 15.1. Initialization and consume analysis

This section checks the initialization, completeness, and Consume legality of values defined by [Values, places, and storage](03-types-and-values.md#34-values-places-and-storage).

### 15.1.1. Storage, state, and responsibility

Track initialization state and destruction responsibility per place:

| State | Meaning | Read / borrow / Copy / Move | Write to `let` | Write to `var` |
| --- | --- | --- | --- | --- |
| Uninitialized | No initialized value is held | Forbidden | Only if never initialized | Initialization |
| Initialized | An initialized value is held | Subject to Type and access rules | Forbidden | Replacement |
| Moved | The former value/capability and responsibility were transferred | Forbidden | Forbidden | Reinitialization |

Moved records the source's history, not whether the destination value is still alive. Track a `let` place's first initialization separately; neither Move nor internal Destruction resets it. This revision provides no general user operation to explicitly destroy a place and reset that history. State alone grants no access or write permission.

Every read, borrow, Copy, or Move requires initialization on every incoming Runtime Reachability path (§14.9.2). Unreachable source uses the separate checking state in §14.10.3; absence of an execution path does not restore an unusable value. `let` permits one initialization per binding lifetime on each path; `var` permits later initialization/replacement under ordinary permissions. Fields also obey their dedicated access rules.

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

Track two facts independently: **construction completion**, recording successful completion of initialization, and **current completeness**, requiring every stored component to be Initialized and complete. A **complete value** satisfies both. For a derived structure, components are its base subobject and its own Fields; track completion separately at each base/derived layer. For an enum, only the active Case's payloads are components, and [Case construction](06-declarations-and-containers.md#632-case-construction-and-resolution) commits completion. Inactive Cases require no initialization. This revision has no optional-to-initialize stored fields. Computed Properties are not components and require no storage initialization.

Only the [constructor phases](06-declarations-and-containers.md#623-constructors) commit completion: validate success, finish cleanup with storage alive, then commit before exposing/transferring the value. No user operation commits early or resets completion; an incomplete exit or Abort never commits. Initialized fields alone do not skip remaining work. A complete field/base keeps its own completion and destruction responsibility even if its enclosing layer never completes.

Before completeness, whole-value reads, Copy, borrowing, Move, and exposure are forbidden, including ordinary accessors taking the whole `self`. Direct operations on initialized fields follow their own access rules. After a completed construction followed by Partial Move, direct [Field operations](11-properties.md#1112-move-paths-and-inherited-fields) are allowed; this does not authorize initial-construction access.

Partial Move changes current completeness, not the construction-completion fact. Permitted reinitialization of all missing fields restores completeness without rerunning a constructor. If a field cannot be reinitialized, that value remains incomplete and cannot be used/transferred as a whole; its remaining Initialized parts can still be used and cleaned up. No separate permanent-incomplete state is defined. Whole-value Move transfers its construction information; whole replacement uses the new value's information.

Tuple and array construction places elements in increasing element-index order. Each element acquires its own initialization and responsibility only when placement completes normally; a partly built element is tracked recursively. The aggregate commits completion after all elements are placed. If an element expression leaves the construction by ordinary control transfer, first secure that transfer's result, then clean the abandoned construction's remaining elements in decreasing index order. Previously moved arguments and completed side effects are not rolled back. Raw uninitialized memory is not an alternate safe construction syntax. Fixed arrays additionally require [whole initial construction](04-arrays-indexing-and-slices.md#43-initialization-and-inference); static-path repair after completed construction remains permitted.

### 15.1.3. Move paths and partial move

A **Move Path** is a statically trackable path with independent initialization state and destruction responsibility. Initial paths include Fields reached through statically known base-subobject paths, Tuple elements, fixed-length array indices recognized by the literal-only ConstantIndexExpression rule below, and combinations of these. Runtime indices, dynamic containers, and user indexers are not added even for literal indices. Do not use optimization-derived constant propagation or arbitrary integer proofs to expand the accepted paths.

**Constant fixed-array indices.** A ConstantIndexExpression is one nonnegative integer literal token, optionally enclosed in any number of grouping parentheses. All integer bases and digit separators allowed by §2.6 are accepted. Its value must fit the ordinary expected isize Type; remove separators and decode the literal magnitude using the lexical integer rules. This recognition has no arithmetic, conversion, name lookup, general constant evaluation, or target-dependent environment evaluation.

~~~ebnf
ConstantIndexExpression := IntegerLiteral | "(" ConstantIndexExpression ")"
~~~

After resolving a fixed-array Type `[N of T]`, an index recognized this way forms a static element Move Path only when its value n satisfies `0 <= n < N`. Path identity uses the numeric value, so `1`, `0x1`, and `(1)` designate the same element. No optimizer result changes this classification. The same rule defines constant fixed-array indices for overlap analysis (§15.6.2); it grants neither a path through a dynamic collection nor permission to Move through a borrow.

| Index expression | Static fixed-array Move Path |
| --- | --- |
| `0`, `(0)`, `((0))`, `0x1` | Yes, when fitting isize and in bounds |
| `1 + 1`, `-1`, `+1`, `3@isize` | No; operators/conversions are outside this grammar |
| `if condition => 1 else => 2`, an immutable Name, `^1` | No; selection, name propagation, and from-end resolution are not literal recognition |

An out-of-range literal supplies no element path. This does not itself make an otherwise valid evaluated index operation a compile-time error: its bounds check follows §4.6 and §17.3.4 and Aborts if executed. An operation requiring a statically eligible Move Path is rejected if no such path exists, independently of whether its bounds check could fail. Literal-fitting errors remain ordinary compile-time errors. An expression such as array[1 + 1] is therefore still usable for ordinary permitted reads, but cannot gain Partial Move eligibility or prove disjointness from optimization.

Within an owned match Subject, selected Case payload positions also form Move Paths under [match acquisition](#1516-match-acquisition-and-lifetime). This does not expose general payload access or Partial Move from the caller's enum.

A Move Path defines tracking granularity, not access permission:

| Source access | Partial Move |
| --- | --- |
| Tuple element / constant-index fixed array element | Direct place acquisition normally Moves a non-Copy value |
| Stored Property slot | Standard acquisition Copies F when Copy, otherwise Moves under §11.1 permissions and Move Path rules. |

```kimi
var pair: (string, i32) = ("Alice", 30)
let name = pair.0  // Partial Move; pair is incomplete.
let age = pair.1   // Remaining initialized part is usable.
// let all = pair  // Error: incomplete.
pair.0 = "Bob"
let all = pair     // Complete again.
```

Do not Move a non-Copy referent or subpart through `ref`, `uniq`, `objref`, or `objuniq`, leaving the borrowed place Moved/Uninitialized, even if a later reinitialization is planned. Exclusive access does not transfer ownership. Copy acquisition leaves the source initialized; it is not extraction.

User-defined `deinit` assumes a complete value. Reject Partial Move that invalidates this assumption for the aggregate itself or any enclosing ancestor, including nested paths, methods, and Destruction. A complete owned value whose own Type has `deinit` may move as a whole.

Use [Exchange or Swap](#157-initialization-preserving-exchange), or a Type-specific operation, when ordinary extraction is forbidden. Exchange preserves initialization; Type-specific invariants remain the implementation's responsibility and may require restricted storage access. A Field path does not bypass an intervening computed getter.

### 15.1.4. Consume verification and representation

Separate two static checks; they are not runtime fallback stages:

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

The declaration kind is structural; accessibility depends on the use site. Constraints can prove structural facts, not current initialization or absence of Loans. Unknown structural facts follow [generic Access Effect resolution](08-generics-constraints-and-contracts.md#89-generic-access-effects); no new Consume contract syntax is defined.

An ancestor may be incomplete if the target remains Initialized and complete, and can be located without whole-value access to that ancestor. Whole-receiver reads/borrows remain forbidden. User-defined `deinit` can make a path structurally ineligible; also check the actual ancestors at each use. Moving a complete value as a whole is distinct from Partial Move.

Track per-path state, destruction responsibility, first initialization of `let`, construction completion, and current completeness across branches, loops, transfers, and `defer`. Apply [Destruction lifetime checks](#1566-destruction-lifetime-checking). Raw-pointer operations need not recover or repair an untracked original owner's responsibility.

Lowering may elide transfers and temporary storage or use conditional cleanup flags only while preserving values, abstract place identity and lifetime, Move state, Loans, Origins, destruction responsibility, and specified failures. Optimization must not change which programs or Move Paths are legal.

### 15.1.5. Movable places

A **Movable Place** permits ownership/capability transfer from its current value. Safe direct sources are owned root Places, Tuple elements, fixed-array elements at eligible constant indices, and authorized stored Property slots. Require an Initialized complete target, no conflicting Loan, accessible consuming operations, and valid construction, partial-Move, Origin, and deinit conditions.

Borrowed referents, object fields, static storage, unsupported indices, and hidden Property storage cannot supply safe extraction. Generic owned arguments may move as whole values without an extra Contract. Raw dereference retains its Unsafe obligations.

Ordinary acquisition Copies Copy Types and otherwise Moves. A Move marks the source Moved and transfers its complete Type, dependencies, and destruction responsibility; moving a reference transfers capability, not referent ownership. A let binding may supply a Move but cannot be reinitialized. Restoring var needs write permission. There is no forced Move of Copy Types.

```kimi
let number: i32 = 10
let copied = number // Copy; number remains initialized.
let resource = makeResource()
let taken = resource // Non-Copy Move; resource is now Moved.
```

Getter results are acquired as results, never by moving hidden storage. An already owned temporary transfers to its destination under §3.6. Borrowing its destination does not restore the original source.

### 15.1.6. Match acquisition and lifetime

`match E` evaluates `E` once and initializes an internal **Subject Place** by ordinary whole-value acquisition: Copy for a Copy Type, otherwise Move. Materialize an existing Temporary Value without an extra acquisition. This happens before arm selection regardless of bindings, Wildcards, or whether any arm succeeds. Optimization cannot change the original Place's Move state, lifetime, or Loans. A custom/computed/required get subject invokes its getter once; standard stored get uses permitted Place acquisition.

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

Once an arm is selected, initialize its body locals left to right from [Candidate Places](14-control-flow.md#1483-guards). An unguarded arm is selected immediately on Pattern success; a guarded arm requires successful guard cleanup first.

| Access to the Binding position | Acquisition |
| --- | --- |
| Subject itself or owned Tuple/payload path without traversing a borrow | Ordinary Copy/Move of the stored complete Type |
| Element reached by structurally matching through `ref/T` | Shared reading under the [shared element-read rules](04-arrays-indexing-and-slices.md#466-slice-operations-and-element-results), without calling a getter |
| Wildcard position | No acquisition; existing responsibility remains with the Subject or referent's owner |

Resolve dependent read Types and Copy/Move/Borrow/Reborrow effects by the [Generic Access Effects deadline](08-generics-constraints-and-contracts.md#89-generic-access-effects). Borrowed access cannot Move a non-Copy referent or grant exclusive authority. In `.Some(let r)`, a stored `ref/T` is copied as a reference; a nested Case Pattern through that reference restricts descendant acquisitions to shared access. New borrows require valid referents and owners; copied references retain their existing Origins/Loans.

For `wrapper: Option<Result<i32, i32>>`, matching `wrapper@ref` with `.Some(let r)` Copies the Result into r; borrowing the subject does not force payload bindings to be borrows. A change to payload Copy capability can change binding Types and generic-body validity. No explicit borrow-binding or ordinary payload projection syntax is provided. APIs that need such a borrow may instead accept the whole enum by reference, potentially changing their public Signature; [Pattern acquisition selection](appendices/D-deferred-features.md#d1-enum-and-pattern-extensions) remains a design boundary.

For owned decomposition, track each selected Case Identity and positional payload index as a Move Path inside the Subject, including recursive decomposition. Track remaining initialization and destruction responsibility after each acquisition; do not apply these internal paths to the caller's original enum or invent ordinary payload projection syntax.

The Subject lasts for the match evaluation. Secure a match result or outward transfer value first, clean the arm scope under ordinary Scope Exit, then destroy the Subject's remaining initialized parts. Body bindings enter scope left to right and are destroyed in reverse order, before the Subject; body locals and `defer` retain normal reverse registration order. A Wildcard does not destroy immediately. Unselected arms acquire no body ownership. Match is exhaustive and has no unmatched normal-completion path. Each later step requires earlier cleanup to complete normally; Abort does not unwind.

Borrowing through a borrowed Subject depends on the referent and original Loan, not the temporary slot storing that reference. Such a borrow may be returned when its original contract allows. A new borrow into an owned Subject's payload depends on the Subject and cannot escape match. Copying or moving a stored external reference retains that reference's external Origin instead. Returning a reference never extends its lifetime, and destroying a stored non-owning reference never destroys its referent. Guard-specific escape restrictions remain [stricter for new candidate-dependent Loans](14-control-flow.md#1483-guards).

## 15.2. Origin expressions and ordering

The basic meaning of Origins and Loans is defined in [Origins and Loans: overview](03-types-and-values.md#37-origins-and-loans-overview). This section defines annotation expressions and their ordering.

### 15.2.1. Origin expressions

An Origin is the set of program points at which a borrow is guaranteed to be valid.

| Kind     | Examples                | Meaning                                         |
| -------- | ----------------------- | ----------------------------------------------- |
| Concrete | `x`, `self`, `x.source` | Origin carried by a parameter, receiver, or local value |
| Abstract | `source`, `left`        | Origin parameter declared by a function or type |
| Static   | `static`                | Built-in maximum Origin                         |

The syntax is:

```text
origin-expression := Name
                   | origin-expression '.' Name
                   | static
                   | origin-expression 'and' origin-expression
```

A direct safe-borrow parameter, receiver, or local used as an Origin denotes its value's outer borrow Origin, never the lifetime of the variable's storage:

```kimi
func first(x: ref/T) -> ref/T from x
```

`x.source` denotes the abstract Origin `source` carried by `x`. Qualification is required so that values of the same Origin-bearing type remain distinguishable:

```kimi
struct View<T> origin source
    func get(self: ref/Self) -> ref/T from self.source
```

The same projection applies to Origin-bearing local values; ordinary lexical visibility applies, and executable uses require definite initialization. A bare value name with no outer safe-borrow Origin is invalid as an Origin, even if its value has an owned outer Type with borrowed contents; select a declared Origin with `x.source` instead. Thus `from r` for a local `r: ref/T` always refers to r's referent. Borrowing r's slot uses an explicit layered Borrow (§13.5.5) and infers that slot's Origin. Storage Origins of owned locals likewise remain compiler-internal and are inferred from initialization. Local values cannot supply Origins in public signatures or outside their lexical scope.

### 15.2.2. Ordering and intersection

`o1 : o2` means that `o1` outlives `o2`: `region(o1) ⊇ region(o2)`.

The relation is reflexive and transitive. `static` outlives every Origin.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

Consequently, the intersection contains no region outside either operand; it may equal an operand. A result declared `from x and y` is valid only in the region common to both inputs.

Normalize intersections using these laws, with outlives simplification requiring proof under the [limited Origin solver](#1534-generic-origin-inference):

```text
a and b         = b and a
(a and b) and c = a and (b and c)
a and a         = a
a : b implies a and b = b
```

For a fixed binding and proof environment, flatten intersections, replace proven-equal or mutually outliving Origins by the least stable representative, remove duplicates and operands proven to outlive another operand, and sort the remainder. Use a deterministic total order based on stable Binding Identity, including declaration binder and parameter position where applicable, not spelling, input traversal order, or memory address. A singleton is its operand.

Renormalize after substitution or changes to proof evidence; cached reductions must validate their proof dependencies. This is a canonical form under the permitted rules, not a general semantic-equivalence test. Keep input Loan dependencies separately: simplifying an Origin expression never removes a distinct input's Loan.

### 15.2.3. `static` and `Owned`

```kimi
func empty() -> ref/string from static
```

A shared borrow from `static` has no non-static lifetime dependency and must satisfy the [static-source rules](11-properties.md#1132-static-storage). A new safe borrow of mutable static storage has a finite Origin. Safe code cannot derive `uniq/T from static` from longevity alone: an exclusive borrow also requires a unique Loan anchor. An abstract Origin whose Loan requirement is `uniq` cannot be bound to `static` in safe code.

`static` describes an Origin. `Owned` expresses independence from non-static lifetime dependencies, not ownership Semantics or permission to allocate storage:

```kimi
func register<F>(f: F)
    F is Owned
```

A valid complete Type T is `Owned` exactly when every Origin in **OwnedOrigins(T)** equals static; an empty set satisfies the condition. OwnedOrigins is the conservative dependency closure of the Type's outer Origin, its Semantics target (value referent, object payload or View Target, or raw-pointer pointee Type), all instantiated Type and Origin arguments (including unused slots), bases, stored Fields, enum payloads, Tuple components, array elements, and concrete Closure captures. Expand aliases and substitute declaration bindings before traversal. Recursive Types use the structural fixed-point rules, not circular conformance evidence. A base or runtime-contract view contributes its visible Type/Origin arguments; its hidden payload was certified at erasure (§15.8.1).

Callable Types contribute every fixed Origin in their complete Type: a Function Item's bound generic and Origin arguments, a concrete Closure's captures and fixed signature Origins, and fixed Origins written in a common Function Type's parameter/result Types. Only Origins bound per call, such as the direct-input quantification of §8.6 and §15.4, are excluded because they have no fixed binding to prove. A common Function Type's hidden environment is certified Owned at erasure. An Owned proof never infers or rewrites a callable's per-call contract.

Use established outlives facts: `a : static` proves a equal to static because static is the maximum Origin. An unbound/unproved abstract Origin yields Unknown, not proof of `not Owned`; resolve required evidence by the ordinary deadline. A generic definition proves Owned for its own Type parameters and abstract Origins only from its declared Constraints and bounds (§8.10); that proof cannot wait for instantiation. Empty containers and unselected Cases do not weaken this Type-level check. In particular, `unsafe/(ref/i32 from local)` and a wrapper with that non-static Type argument cannot prove Owned even if no safe-borrow Field is visible. Traversing a pointee Type neither dereferences a pointer nor creates a Loan; unsafe implementations must still expose their actual lifetime dependencies and uphold pointer validity.

This revision requires Owned at static storage, concrete payload erasure into base/runtime-contract views, common Function Type environment erasure, cyclic-factory payloads (§13.5.8), and explicitly declared Owned Constraints. A lifetime-hiding library API states that requirement explicitly; the compiler does not infer an "indefinite retention" capability from a private body. Ordinary storage and concrete object allocation impose no blanket Owned requirement. Owned never discharges acquisition, Loan, destruction-order, unsafe, or concurrency checks.

## 15.3. Abstract origins

Functions and types may declare abstract Origin parameters separately from type parameters:

```kimi
func unwrap<T> origin s(v: View<T> from (source => s))
    -> ref/T from s

struct View<T> origin source
    let value: ref/T from source
```

Function Origins are universally quantified. Origin parameters occupy a namespace distinct from type parameters.

An Origin parameter may have one bound, `name : target`, meaning that `name` outlives `target`. The target is `static` or an abstract Origin visible in the declaration, including any binder in the same list; bind the whole list before resolving bounds. A bound is not a default Origin argument. Duplicate binders and unknown targets are errors. Reflexive bounds are redundant; cycles require equal regions under §15.2.2.

```kimi
struct Holder<T> origin stored
    var value: ref/T from stored

func store<T> origin a, b : a(
    holder: uniq/(Holder<T> from (stored => a)),
    value: ref/T from b)
    holder.value = value
```

Here `b : a` permits shortening the stored reference to `a`. The holder receiver's outer Origin is independently inferred as an input Origin; it does not replace the stored Origin `a`.

Bounds are part of the declaration contract for functions and Origin-bearing Types, including enums and Contract method requirements. Check bodies assuming the declared bounds, and prove the substituted bounds at every call or Type use with the limited solver (§15.3.4). Unknown proof is an error by the ordinary finalization deadline; the body cannot infer additional caller requirements. A Type's members and constructors inherit its bounds. Bounds grant no Loan, initialization, or access permission.

Bounds do not distinguish overloads or specialization keys. Contract implementations must admit every Origin binding allowed by the requirement; specializations inherit the original bounds. Container fragments repeat the same bounds (§6.1.2). Preserve bound identities and proof dependencies in artifacts and invalidate affected uses when they change. No standalone outlives clause, bound on an implicit input Origin, or bound declaration inside a Function Type/Callable signature is introduced; use explicit function Origin parameters to relate inputs.

### 15.3.1. Origin arguments

Named Origin arguments use `from (...)` and `=>`:

```kimi
struct Pair<A, B> origin left, right
    let a: ref/A from left
    let b: ref/B from right

Pair<A, B> from (
    left => a,
    right => b)
```

Named argument lists require parentheses, even for one argument. For a Type with exactly one Origin, `View<T> from v` abbreviates `View<T> from (source => v)`.

Omitted Origin arguments follow the [position-specific rules](#154-origin-elision-and-return-contracts). Parameter Types and instance Field Types require explicit arguments for Origin-bearing aggregates; local initializers may infer them, and result Types use result-Origin elision. No argument defaults to `static` merely because it appears in a generic Type argument. References to the containing `Self` retain that Type's already-bound abstract Origins and do not introduce a new omitted argument list.

A named Origin argument list may specify only some of the target Type's declared Origins. Resolve names against that declaration; reject unknown names and duplicate bindings, even when duplicate expressions are identical. Apply the enclosing position's omission rule independently to each unspecified argument. Written arguments remain fixed constraints and are neither replaced nor included as additional inputs for result elision. A partial list therefore cannot omit a required argument in a parameter or instance Field Type. The single-Origin shorthand is a complete binding, not a partial list. Binding correspondence follows declaration identity, not list order.

```kimi
// Pair<A, B> declares Origins left and right as above.
func example<A, B> origin a, b(p: Pair<A, B> from (left => a, right => b))
    let local: Pair<A, B> from (left => a) = p
    // Infer right from initialization and ordinary constraints; left stays bound to a.

func invalid<A, B> origin a(p: Pair<A, B> from (left => a))
// Error: parameter Origin argument right must be explicit.
```

### 15.3.2. Variance

These are static subtype rules under [Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations). They do not create or authorize a value operation; acquisition and existing Loan obligations must be checked separately.

The compiler infers Origin variance from all occurrences and solves recursive types to a fixed point. Explicit variance annotations are not allowed.

| Position                 | Origin                   | Core         |
| ------------------------ | ------------------------ | ----------------- |
| `ref/T from o`           | Covariant in `o`         | Covariant in `T`  |
| `uniq/T from o`          | Covariant in `o`         | Invariant in `T`  |
| Function parameter       | Reverses polarity        | Contravariant     |
| Function result          | Preserves polarity       | Covariant         |
| Interior-mutable storage | Representation-dependent | Usually invariant |

For an Origin parameter `p` of `S`:

- covariance permits `S from (p => o1) <: S from (p => o2)` when `o1 : o2`;
- contravariance reverses that relation;
- invariance requires equal Origins.

The direct borrow rules are:

```text
o1 : o2
--------------------------------
ref/T from o1 <: ref/T from o2
uniq/T from o1 <: uniq/T from o2
```

`uniq/T` remains invariant in `T`. These variance rules do not add ordinary inheritance upcasts or value operations for callable signature matching; use the separately defined [object adaptations](13-operators-and-assignment.md#1357-object-upcasts).

### 15.3.3. Loan requirements

Each abstract Origin has an inferred Loan requirement:

```text
none < ref < uniq
```

Using an Origin in `ref/T` requires `ref`; using it in `uniq/T` requires `uniq`. Multiple uses take the stronger requirement, and requirements propagate through nested Origin-bearing types.

```kimi
struct View<T> origin source
    let value: ref/T from source       // loan(source) = ref

struct MutView<T> origin source
    let value: uniq/T from source      // loan(source) = uniq
```

The requirement determines which caller-side Loan must remain active while a returned or stored Origin-bearing value is live. It is not an additional Copy classification condition: [Copy capability](03-types-and-values.md#351-copy-capability-and-explicit-duplication) is structural, while actual Loan conflicts are checked at each use.

### 15.3.4. Generic Origin inference

For both ordinary and pair slots, put inference variables at each unresolved Origin position in the complete Type W. Apply variance recursively to nested Types and aggregate mappings. Never turn an explicitly fixed Origin back into an inference variable.

| Position | Constraints and candidate solution |
| --- | --- |
| Covariant | Inputs a and b contribute `a : alpha` and `b : alpha`. Without an equality constraint, use the meet of all upper bounds: their longest common region |
| Invariant | Require proven Origin equality; do not replace invariant inner positions of `uniq/V` with a meet |
| Contravariant | Reverse constraint direction. If one lower bound provably outlives every other lower bound, choose that least upper bound; do not invent a union of incomparable bounds |
| Mixed or cyclic | Combine equality and ordering constraints; require a representable, unique principal solution modulo proven Origin equivalence |

A **principal solution** is the most general permitted solution under the Type's variance and fitting relation. Multiple shorter solutions do not make a covariant inference ambiguous: choose the longest common region. Reject inference when no principal solution is expressible or only incomparable candidates remain; require an explicit annotation.

The initial solver uses equality substitution, reflexivity and transitivity of established outlives facts, and the common-lower-bound laws of `and`. Use the forced equality for invariant positions, the meet of upper bounds for covariant positions, and the comparable-lower-bound rule above for contravariant positions. Substitute candidates into every constraint, including inner dependencies and use requirements. Do not enumerate arbitrary regions or use general theorem proving. An unresolved dependency may be retained only under [deferred-obligation rules](08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations).

```text
choose<s/T>(x: s/T, y: s/T) -> s/T
Inputs:      ref/i32 from a, ref/i32 from b
Constraints: a : alpha, b : alpha
Binding:     W = ref/i32 from (a and b)
Result:      W, retaining both input Loan dependencies
```

This example has no additional constraints. Changing input order does not change the solution; an expected result cannot extend a or b. Even equivalent Origin regions retain separate input Loans. Exclusive use still requires a unique Loan anchor and valid acquisition/Reborrow after Origin inference succeeds.

## 15.4. Origin elision and return contracts

Origin omission depends on the position of the complete Type. These rules apply to `ref`, `uniq`, `objref`, and `objuniq`, after alias expansion and normalization of grouping and redundant owner prefixes. They do not create a safe-borrow Origin for `unsafe/T`.

| Type position | Meaning of an omitted Origin |
| --- | --- |
| Direct borrowed parameter or borrowed receiver | Introduce an independent input Origin for that input's outer borrow layer. |
| Function result | Apply the result-Origin rules below; anonymous whole-result inference retains its existing rules. A named function with no result annotation returns Unit. |
| Local binding with inferred Type | Infer the Type, Origin dependencies, and Loans from the initializer under ordinary acquisition rules. |
| Explicit local Type containing a borrow or Origin-bearing aggregate | Infer omitted Origins and Origin arguments from the declaration initializer and ordinary Origin/Loan constraints. Without an initializer, omission is a compile-time error. |
| Instance Field | Require explicit bindings for all borrow layers and required Type Origin arguments; do not infer the storage contract from initialization. |
| Static Field | Require Owned and §11.3.2's static-source rules. Omitted shared borrow Origins and aggregate Origin arguments with Loan requirement `none` or `ref` default to static; an exclusive borrow layer or `uniq` Loan requirement is rejected. Preserve bound Type/Origin arguments and callable contracts. |
| Generic Type argument or nested value Type | Recursively apply the enclosing position's rule; being a Type argument does not introduce a separate default. |
| Enum Case payload declaration | Apply the instance storage rule to every payload element, including nested Origins and required aggregate arguments; see [enum payloads](06-declarations-and-containers.md#63-enums). |
| Constructor parameter | Apply the ordinary parameter rule; the constructed value retains the containing Type's declared Origin contract under [construction](06-declarations-and-containers.md#623-constructors). |
| Property accessor | Complete actual getter results and setter inputs independently by function elision (§11.3). Stored custom signatures must then match complete storage T. Computed/required signatures have no shared storage contract. |
| Getter result, explicit or defaulted from P | Apply function result elision with the actual getter receiver; preserve already bound dependencies (§11.3). |
| Adaptation Target | Infer result Origins from the operand, operation, and constraints under [Adaptation Targets](13-operators-and-assignment.md#1351-forms-and-adaptation-targets); this is not signature result elision. |
| Callable constraint signature | Apply its limited per-call direct-input quantification and result restrictions under [Callable constraints](08-generics-constraints-and-contracts.md#86-callable-constraints), rather than recursively quantifying every nested borrow. |

An implicit input Origin is universally quantified and supplied by the caller’s argument/receiver, not the parameter variable’s lexical lifetime. Direct borrowed inputs introduce independent Origins unless explicitly related; only those outer Origins participate in result elision. Inner borrow and aggregate/generic argument Origins must be explicit and introduce no extra implicit inputs. Bound generic Types and Self preserve their dependencies. Expected callable signatures and [Callable input quantification](08-generics-constraints-and-contracts.md#86-callable-constraints) follow their own rules; no general higher-ranked Origins are added.

For locals, inference means satisfying ordinary subtyping, variance, outlives, and Loan constraints, not requiring literal equality with the initializer's Origin. Permitted shortening remains available. The declaration fixes the local Type and its Origin constraints; later assignments must satisfy that contract and cannot extend a source lifetime or erase a retained Loan dependency. Explicit Origin annotations remain constraints on the initializer and all subsequent assignments.

Local Origin omission requires an initializer, but inference may still fail. Bind each omission to a fixed inference variable and initializer-derived constraints at declaration; later assignment cannot supply a missing annotation. Later uses constrain those variables without reopening Type inference. Region/Loan constraints may remain symbolic during analysis; generic obligations may await permitted substitution/instantiation.

Resolve non-generic omissions before completing body Origin/Loan analysis, and generic obligations before instantiation finalization. Failure to determine or validate the contract by its deadline is a compile-time error requiring an annotation or corrected constraints. Equivalent valid region solutions need not identify one unique point set. Never replace uncertainty with static, an invented abstract Origin, or erased dependencies; static defaults only where elision explicitly allows it.

Instance storage exposes the containing Type's declared Origin contract, including dependencies already bound within complete generic Type arguments. Bind directly written borrow dependencies to declared abstract Origins or explicitly to `static` where valid. An exclusive borrow still needs a unique Loan anchor. Initializers and constructors satisfy this contract; they do not infer it.

```kimi
struct Box<T>
    let value: T
// Box<ref/i32 from a> retains a through its complete Type argument.
// No synthetic named Origin parameter is added to Box.
```

The constructed Box's lifetime, acquisition, and destruction retain that dependency. This does not permit a directly written field `value: ref/i32` to omit its Origin. Static storage can retain the Box when its complete Type proves Owned and its values satisfy §11.3.2. Finite layout, Copy derivation, and Object payload erasure remain separate checks.

**Ordinary storage.** Array, Dictionary, Tuple, fixed array, struct, enum, concrete object payloads (`obj`/`rc`/`arc`), and concrete Closure environments accept valid complete stored Types without a blanket Owned or Storable requirement. Preserve Type/Origin arguments and value-level Loan identities, anchors, and Reborrow relationships through acquisition, storage, Move/Copy, calls, and destruction. Conservative OwnedOrigins checks include Type-level dependencies that create no actual Loan; actual Loans still require provenance. Heap placement neither extends a referent's lifetime nor changes these rules. Loan liveness follows required uses and observable destruction (§15.6), not merely the enclosing lexical scope.

For example, `func singleton<T>(value: T) -> Array<T>` with body `return [value]` is valid without Owned or Copy: acquire T once and propagate its complete dependencies. The same applies to a body-local Array even when no Array appears in the public signature. Verify all admitted Types at definition time under §8.10; representation obligations cannot hide new capability requirements. A copied shared-reference element retains its original referent's lifetime, while a borrow of the element slot is also bounded by Array storage (§4.6.6).

```kimi
func f<T>(x: ref/T, y: ref/T)
// Independent input Origins x and y.

func nested<T> origin inner(x: ref/(ref/T from inner))
// The outer borrow has implicit input Origin x; inner is explicit.

func invalid<T>(x: ref/ref/T) // Error: the inner Origin is omitted.

struct View<T> origin source
    var value: ref/T from source

func use<T>(x: ref/T)
    var local: ref/T = x // Infer an Origin satisfying initialization and use constraints.
    var missing: ref/T  // Error: no initializer to infer the omitted Origin.

group Global
    let number: i32 = 1
    var counter: i32 = 0
    let value: ref/i32 = Global.number@ref    // Omitted Origin is static; immutable source.
    let invalid: ref/i32 = Global.counter@ref // Error: a mutable static source has a finite Origin.
```

When a result Origin is omitted, the compiler applies these rules in order:

1. If the complete result Type contains no borrow or required Origin argument, including dependencies retained through aggregate fields, elements, and generic arguments, no result-Origin constraint is generated. An owned outer Semantics does not make an Origin-bearing aggregate borrow-free.
2. If there are directly borrowed parameters, each omitted result Origin becomes the meet of all their Origins.
3. Otherwise, an omitted shared result Origin is `static`; an omitted aggregate Origin argument likewise becomes `static` only if its inferred Loan requirement is `none` or `ref`. If that would create an exclusive static borrow or bind a `uniq` Loan requirement to `static`, an explicit valid Origin is required.

Apply these rules independently to each omitted borrow-layer Origin and required aggregate Origin argument, including unspecified entries of a partial named list. Preserve explicit bindings and already-bound generic dependencies. Check the completed Type's outlives, variance, and Loan requirements; elision is not permission to weaken an invariant position or bind an exclusive Loan requirement to `static`. No result Origin is inferred from a named function's body.

Examples:

```kimi
func first(x: ref/T) -> ref/T
// result Origin: x

func choose(x: ref/T, y: ref/T) -> ref/T
// result Origin: x and y

func empty() -> ref/string
// result Origin: static

func view<T>(x: ref/T) -> View<T>
// View<T> declares Origin source: result is View<T> from (source => x).
// Its owned outer Type does not suppress the retained borrow dependency.

func pair<A, B>(x: ref/A, y: ref/B) -> Pair<A, B> from (left => x)
// left remains x; omitted right becomes x and y.
```

Only direct borrowed parameters participate in rule 2. Origins nested in aggregate inputs must be selected explicitly:

```kimi
func get<T> origin s(v: View<T> from (source => s)) -> ref/T from v.source
```

An explicit `from` clause overrides elision only for the borrow layer or named Origin arguments it binds; omitted bindings elsewhere still follow the rules above. Thus this result depends on `self`, not on the conservative meet `self and key`:

```kimi
func lookup(self: ref/Self, key: ref/Key)
    -> ref/V from self
```

Whole-result inference for anonymous functions also infers environment-derived Origins and Loans under [Closure result rules](#1582-closure-dependencies-and-call-results); an explicit annotation retains the elision above.

### 15.4.1. Return contracts

A declared return Origin limits the dependency visible to callers without requiring a borrow from that specific input. Every explicit or implicit result, including unreachable ones, must subtype the declared result Type under [result validation](14-control-flow.md#149-result-validation) and [reachability](14-control-flow.md#1492-reachability).

For example, `ref/T from static` may satisfy `ref/T from x` because `static : x`, provided the Origin position is covariant. Invariant positions require equality, while contravariant positions reverse the subtype direction.

`from x and y` is deliberately conservative in two ways:

- the result region is `region(x) ∩ region(y)`;
- Loans for both possible sources remain active while the result is live.

```kimi
let r = choose(a, b)
b.mutate()       // Error: the Loan on b is still active.
use(r)
```

The caller cannot rely on which argument the implementation actually selected. An Origin-bearing result type with distinct Origin parameters can preserve more precision.

## 15.5. Exclusive origins

An exclusive borrow requires both a valid Origin and a unique Loan anchor. An Origin proves longevity but not uniqueness.

A shared borrow may be returned from a stored Origin:

The following independent member-signature excerpts are written inside `View<T> origin source`; bodies are omitted to focus on Origin and Loan contracts.

```kimi
struct View<T> origin source
    func get(self: ref/Self)
        -> ref/T from self.source
```

Returning `uniq/T from self.source` from `self: uniq/Self` is invalid because detaching the result from the current `self` Loan could allow a second exclusive borrow:

```kimi
struct View<T> origin source
    func bad(self: uniq/Self)
        -> uniq/T from self.source       // Error
```

One valid form consumes the Origin-bearing owner:

```kimi
struct View<T> origin source
    func into_uniq(self: Self)
        -> uniq/T from self.source
```

Moving `self` prevents reuse of the capability.

Alternatively, reborrow through the current exclusive receiver:

```kimi
struct View<T> origin source
    func get_uniq(self: uniq/Self)
        -> uniq/T from self
```

The parent Loan remains active, and access through it is suspended, while the returned reborrow is live.

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

These projections describe direct Field and lowered storage Places. Base/Field identities preserve inherited paths; no ordinary base-reference conversion is implied. Custom/computed/required accessors instead use function boundaries (§11). Parentheses preserve Places. Reading Copies, Moves, or borrows according to context and permissions.

A **region** is a set of program points. Local regions are inferred; Origins in signatures introduce universal regions; `static` is the maximum region.

A Loan is:

```text
Loan = (place, mode, region)
mode = ref | uniq
```

It is active at program point `P` exactly when `P` belongs to its region. Regions follow actual uses rather than lexical scope, providing non-lexical lifetimes:

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
| Well-formedness | Every Origin in `T` observable through `ref/T from o` or `uniq/T from o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described under Calls and Origin propagation. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

### 15.6.2. Place overlap and conflicts

Two places overlap when an operation on one may affect the other. Static place analysis uses only these structural rules for proving non-overlap:

| Places | Result |
| --- | --- |
| Identical place, or a place and an inline subpart | Overlap |
| Independent local roots and their inline parts | Disjoint |
| Distinct inline stored fields, Tuple elements, or different constant fixed-array indices of one aggregate, and their subparts | Disjoint |
| Referents of simultaneously live valid `uniq`/`objuniq` borrows with distinct Loan anchors | Disjoint by exclusivity |
| Other reference dereferences | Follow Loan provenance and apply these rules |
| Anything not decided above | Non-overlap unproven; reject operations requiring proof |

Inline parts exclude pointer/reference referents. Distinct shared-reference or raw-pointer variables alone do not prove independence. Constant fixed-array indices use only the [ConstantIndexExpression rule](#1513-move-paths-and-partial-move), comparing decoded in-range literal values, not general constant evaluation or optimization; runtime index comparisons such as `i != j` do not establish disjointness. No arbitrary integer proof or optimizer result changes acceptance. Array-derived Slices retain the whole-array Loan footprint through reslicing, splitting, and empty views under [Slice lifetime rules](04-arrays-indexing-and-slices.md#465-slice-storage-lifetime-and-permissions). Simultaneous exclusive borrows may be used only through their valid access paths; reborrowing still suspends conflicting parent access.

These are storage rules, not permission to bypass Property accessors. Direct Field operations may borrow disjoint fields separately; custom/computed/required calls retain their receiver footprint.

Each operation is checked against every active Loan on an overlapping place:

| Operation               | Existing `ref` | Existing `uniq` |
| ----------------------- | -------------- | --------------- |
| Read                    | Allowed        | Forbidden       |
| Write or move           | Forbidden      | Forbidden       |
| Create `ref`            | Allowed        | Forbidden       |
| Create `uniq`           | Forbidden      | Forbidden       |
| Destroy the borrowed place | Forbidden      | Forbidden       |

This enforces shared aliasing or mutation, but never both simultaneously.

### 15.6.3. Reborrowing

Borrowing through an exclusive borrow creates a child Loan. While the child is live, the parent remains live but access through it is suspended. Overlapping access is rejected by the normal conflict rules.

**Basic example.**

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
2. instantiates parameter types and checks argument subtyping;
3. proves the substituted declared outlives bounds against caller facts (§15.3), rather than assuming them;
4. instantiates the return type;
5. recursively collects its Origin dependencies and Loan requirements;
6. creates the required caller-side Loans and keeps them active for the corresponding result regions.

This applies to direct borrow results and nested aggregate results:

```kimi
func make(a: ref/A, b: ref/B)
    -> Pair<A, B> from (
        left => a,
        right => b)
```

While the returned `Pair` is live, shared Loans on both `a` and `b` remain active. A dependency requiring `uniq` propagates an exclusive Loan. The spelling `static` alone creates no parameter-root Loan; it does not erase the storage anchors and conflicts of a borrow into static Field storage.

A call's receiver and argument Loans begin as each borrow/reborrow is formed in evaluation order, before later arguments and defaults. In particular, an exclusive receiver is active while explicit arguments are evaluated. Keep receiver/argument borrow protection through the entire call, not merely the callee's last use, and extend it for dependent results. No two-phase reservation exception is defined; intrinsic Exchange/Swap and dynamic collection mutation use the same rule. These language checks supply the common value-borrow attribute proof in §21.5.5.

**Static call effects.** Summarize each callable's potentially accessed static Field Identities and read, shared/exclusive borrow, write, replacement, and destruction effects, including callees, defaults, lazy initialization, and cleanup. Compare them with active caller Loans by normal overlap rules. Borrowed results retain Field anchors and dependency paths: immutable-source borrows may be static, whereas mutable-source borrows must retain a finite Origin under §11.3.2. Static allocation never permits replacing/destroying a borrowed current value.

Summaries distinguish first-access initialization effects from ordinary accesses. A live Loan anchored to a Field proves that Field has completed initialization, so its initializer need not be counted again; this proves nothing about an unrelated Field first accessed by the callee. If a result may derive from several static Fields, retain every possible anchor conservatively, independently of the runtime branch selected.

If `let view = State.text@ref` borrows a mutable static string Field, view has a finite Origin; reject State.reset() while view has a later use if reset may replace that Field. A shared Loan still permits read-only calls. Summarize the whole call conservatively; favorable runtime branches need no special analysis. Compute recursive fixed points before acceptance. Separate/indirect calls consume published validated summaries, or conservatively treat unknown effects as conflicting with every potentially affected active static Loan. Clients need not inspect private bodies. Preserve immutable anchors for shutdown dependencies even after erasure; mutable-source borrows cannot cross an Owned boundary. FFI validity and aliasing obligations still apply.

Retain these static/capture anchors when composing [receiver-preservation effects](12-expressions.md#12442-effect-verification), even when self is not an explicit argument. The same call may affect multiple roots. Published summaries and their dependencies follow §18.3 and §21.3.4.

### 15.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

**Error example.**

```kimi
func bad(x: ref/T) -> ref/T from x
    let local = T.new()
    return ref/local       // Error
```

The local value cannot satisfy the universal return Origin `x`; returning its borrow is a compile-time error.

### 15.6.6. Destruction lifetime checking

The [Destruction rules](16-scope-exit-and-destruction.md#163-aggregate-destruction-and-deinit) and [Scope Exit](16-scope-exit-and-destruction.md#162-scope-exit-destruction) determine responsibility and order. Destruction lifetime checking applies to every Origin/Loan that Destruction may observe and requires validity at each such observation.

```text
DestructorUsePoints(value, origin) ⊆ region(origin)
```

Destruction that observes no Origin/Loan adds no lifetime requirement. Conservatively assume that every user-defined `deinit` observes all reachable Origins even if its body does not use them, and apply the same checking recursively to field Destruction. No relaxation mechanism is defined.

```kimi
struct Logger origin sink
    let out: uniq/Writer from sink

    deinit
        observe(self.out)
```

Here `observe` accepts `ref/Writer`; reading `self.out` shares the stored capability instead of extracting it. `sink` must remain valid during Destruction, even if the `deinit` body were replaced with `()`.

## 15.7. Initialization-preserving exchange

| Operation | Old value | Placement | Result |
| --- | --- | --- | --- |
| Initialization | None | Fill empty storage | Unit for assignment |
| Replacement (`=`) | Destroy remaining old parts | Place after destruction | Unit |
| `Exchange` | Transfer without destruction | Keep target initialized | Old value |
| `Swap` | Exchange both values without destruction | Keep both initialized | Unit |

`Exchange(place, with: value)` and `Swap(placeA, placeB)` denote language-provided intrinsic exchange operations. Their semantic requirements are defined here; final API spellings and resolution remain separate. In examples, `place` denotes **authorized direct storage access**, not permission to bypass a Property getter or expose its private storage. Getter-result acquisition grants no access to backing storage.

### 15.7.1. Evaluation and transfer

- Each target is Initialized and permits exclusive writing. Values have identical complete Types, including Origins; conversions finish before exchange begins.
- Evaluate targets and arguments left to right. A target's exclusive Loan begins when its borrow argument is formed and remains active during later argument evaluation and exchange, just as for an ordinary exclusive receiver call. There is no reservation or delayed-activation exception.
- Exchange itself runs no user code, Destruction, Abort-producing work, or control transfer. Internal empty states cannot be observed by the program. This does not guarantee inter-thread atomicity.
- If argument evaluation fails, do not exchange; apply normal temporary cleanup and preserve Origin/Loan dependencies.

`Exchange` secures its replacement first, then transfers the old target value and responsibility to the result and the replacement's responsibility to the target. Preparing the replacement must not empty the target or create a conflicting borrow. `Swap` transfers both values and responsibilities while keeping both targets Initialized and returns Unit; static non-overlap is required.

```text
Before: target = old, replacement = new
After:  target = new, result = old
```

```kimi
// p denotes an authorized writable i32 place.
Exchange(p, with: p + 1) // Error: later read conflicts with the target Loan.
let next = p + 1
Exchange(p, with: next)  // Valid: computed before borrowing the target.

// x is a writable non-Copy owned value.
x = x                   // Valid: RHS-first Move and reinitialization.
Exchange(x, with: x)     // Error: replacement would empty the target.
```

A Type-specific operation with `uniq/Self` may use authorized field exchange to return the old owned field without leaving the receiver incomplete. It must preserve the Type's invariants and all dependencies.

### 15.7.2. Static non-overlap

Use the structural [place analysis](#1562-place-overlap-and-conflicts). Distinct independent roots, distinct inline fields/Tuple elements/constant fixed-array indices, and valid simultaneous exclusive borrows with distinct Loan anchors can prove non-overlap. Identical or containing places overlap. Follow Loan provenance for other dereferences; different raw pointers or shared-reference variables alone prove nothing.

```text
Function parameters: a: uniq/T, b: uniq/T
Swap(referent(a), referent(b)) // Conceptual storage notation: distinct live anchors.
```

Unknown relationships are rejected. Do not accept `Swap(a[i], a[j])` merely from `i != j`, arbitrary integer facts, or optimization. This bounds the required proof and the accepted programs, not just compiler effort.

## 15.8. Captured and erased dependencies

### 15.8.1. Object payload erasure

Erasing a concrete payload behind a base or runtime-contract view requires its complete data Type to satisfy `Owned` under §15.2.3's conservative OwnedOrigins closure; the handle's own Origin is not this test. Resolve payload Type/Origin arguments and ordinary exclusive-Loan restrictions before erasure. A local object borrow may still have a local Origin when its payload is Owned.

```text
Dog owns only i32/string data -> Owned payload -> base/contract erasure allowed
Dog stores a local ref       -> non-Owned     -> initial erasure rejected
objref/Animal from local     -> borrow remains local even when payload is Owned
```

This is not a blanket `from static` requirement on object handles or exact concrete views. Same-target operations retain existing lifetime rules. An erased view certifies that this check succeeded; later upcasts/casts inherit it without runtime Origin queries. Only proof-covered fixed Origin bindings may be supplied as static in a checked cast (§13.6.2); per-call callable Origins and the handle's outer Origin are not reconstructed. Existing borrowed-field Types remain valid; hiding their non-static dependencies needs a later existential-view design. Owned does not waive pointer validity, Loan, destruction, or concurrency checks.

### 15.8.2. Closure dependencies and call results

A Closure recursively retains every captured value's Origin and Loan dependencies, not merely the lifetime of its creation Block. Moving an owned value with no borrowed contents does not borrow its old local storage. Copying a shared reference, moving an exclusive reference, reborrowing, or acquiring a borrowed aggregate preserves the corresponding external Origins, child Loans, and parent restrictions. Do not collapse independent dependencies or discard them at generic substitution or type erasure. Apply §15.2.3's OwnedOrigins to the captured Types when proving the environment Owned.

Distinguish the Closure's captured dependencies from each call's receiver and result dependencies:

| Result source | Contract |
| --- | --- |
| Borrow of environment-owned data | Depends on the current Closure receiver borrow |
| Copied captured external shared reference | Retains its external Origin |
| Reborrow of captured exclusive reference | Also depends on the current exclusive call Loan |
| External reference value moved out by Consuming call | Transfers that reference's external Origin and capability |
| Borrowed argument | Follows the input/result Origin contract |

When the whole result Type is omitted and inferred from the body, infer its Origin and Loan dependencies too. Explicit result annotations and fixed expected signatures retain ordinary result-Origin elision: the hidden environment receiver is not a new source-level `self` or directly written borrow parameter.

```kimi
let text = makeText()
let get = func [text] () => text@ref
// Internal signature: call(self: ref/Self) -> ref/string from self.
let view = get()
let moved = get // Error if view is still used below.
inspectText(view)

let other = makeText()
let invalid = func [other] () -> ref/string => other@ref
// Error: annotated omitted result Origin is static, not the environment borrow.
```

Join multiple results under normal result validation, retaining all possible Loan dependencies when taking an Origin meet. Reject invariant mismatch, cycles, and unexpressible dependencies rather than weakening them. A repeatable exclusive result keeps its call Loan until needed uses end, preventing conflicting reentry. No result may outlive call-local storage or borrow an environment consumed by that call; moving an external reference value out is distinct and may be valid.

The internal call contract is preserved for concrete generic use; callers do not inspect bodies to rediscover lifetimes. Common Function Types and Callable constraints may return input-dependent borrows, but cannot expose hidden-receiver-dependent results (§8.6).

### 15.8.3. Escape and retention

The Owned guarantee grants no thread-transfer or concurrent-access capability; see the [concurrency design boundary](appendices/D-deferred-features.md#d2-concurrency-memory-model-and-thread-transfer).

Escape describes retention across a creation/call boundary, not a permanent syntactic Closure category. Check returned values, saved fields, other Closures, and indirect callees against destination lifetime and capability contracts. Borrow capture is not inherently non-escaping, and Move capture does not remove nested borrow dependencies. Moving or heap-allocating a Closure cannot extend a local referent's lifetime. Temporaries retain their original expiration.

Non-escaping means that the callee retains neither the callable nor its environment dependencies beyond the call. It does not mean one call, no allocation, or waived Loan checks. `ref/F` and an Owned result let a verified callback-only body use a borrowed concrete environment without erasure, but neither `ref/F` nor Callable alone promises that every environment-derived value is unsavable. A separately compiled callee cannot be assumed non-escaping without a verified contract. Non-escaping declaration syntax and borrowed erased callable views remain deferred.

Loan validity includes later uses, results, and dependencies observed by destruction, not just the last body invocation or the lexical end of a binding. Concrete Closure storage follows §15.4; the Owned boundaries are listed in §15.2.3.

## 15.9. Lifetime design boundaries

This revision does not define:

- abstract Origin parameters on contracts or trait-like abstractions (the [Property getter receiver/result contracts](11-properties.md#1122-computed-properties) do not introduce contract-level Origin parameters);
- existential object views that hide non-static payload dependencies;
- general higher-ranked Origins beyond the direct-input quantification of Callable constraints;
- Origin expressions naming static Places, such as a function result bounded by a mutable static Field; use direct access, an input-bounded result (§11.3.2), a scoped callback, or immutable static storage instead;
- lending iterators;
- cancellation cleanup guarantees.

These features require extensions to the [Ownership and Origin rules](#15-ownership-and-lifetime-analysis) and must not be inferred from this revision.
