# 10. Overload resolution and inference

[Specification index](../SPEC.md)

Container qualifiers are resolved and bound under §9.6.1 before call inference. This chapter infers only a function's own arguments; an omitted outer Container environment is never inferred from call arguments or an expected result. Candidate identity includes the retained environment, and a failed later constraint does not reopen qualifier lookup.

Argument adaptation, expected-result compatibility and acquisition legality are separate judgments ([Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations)). A successful subtype proof neither selects nor authorizes a value operation.

## 10.1. Candidate applicability

Each declaration in the committed function group is checked as follows, within the shared-expectation and body-checking boundaries of §10.5. A nested call or anonymous body is never checked separately for each candidate.

1. Validate the explicit Type-argument count and kinds.
2. Match positional and named arguments and record omitted defaults.
3. Infer Type arguments from the receiver and the explicit arguments and, in the same fixed point, by matching each parameter's fixed expected call signature against its argument's known call signature (§10.5, §10.8).
4. Use an independently known expected result Type to fill the remaining Type arguments, without changing those already fixed.
5. After substitution and before Best Candidate comparison, check permitted argument adaptations, including the Copy proof that a bare Place's by-value acquisition needs (§3.5), Function Types, Constraints and any conditional-member premises (§7.4, §8.4.8).
6. If an expected result exists, reject a candidate whose instantiated result the use position cannot admit (§10.3).

Having no applicable candidate is an error. Candidate checking records plans; it neither executes nor commits runtime Copy/Move, Loans or defaults. Declaration errors, such as unknown Types, malformed Constraints or duplicate Signatures, remain errors even when another candidate succeeds.

Waiting arguments (§10.5), such as nested calls and anonymous function bodies, are checked against the candidates that remain applicable. How a bare Place argument is acquired never depends on which candidate remains: the functions of one Name acquire corresponding parameters of overlapping Types in one mode ([§7.3.1](07-functions-and-callable-values.md#731-parameter-acquisition-shape)).

**Argument matching.** Positional arguments precede named arguments and bind parameters in declaration order. At a direct call, an ordinary parameter accepts a positional argument only when it precedes the `!` boundary or the declaration has no boundary (§7.2). Positional matching never skips a name-required parameter, a parameter with a default, or a parameter supplied later by name, and never searches by Type for another position. A bound method first removes its receiver position from this sequence (§7.3). Named arguments use external names, may be reordered, and cannot bind a parameter twice. An unbound ordinary parameter takes its default if it has one; otherwise the call is a missing-argument error, whatever the boundary. Unknown labels, excess positional arguments and positional arguments targeting name-required parameters are rejected. Explicit arguments are evaluated in source order, then omitted defaults in parameter order under the [declaration-site rules](07-functions-and-callable-values.md#72-parameters-and-defaults); defaults supply no generic-inference evidence. Function-value calls supply every argument positionally.

```kimi
func scale(value: i32 ! by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // Valid; evaluate by before value.
scale(3, value: 4)     // Error: value supplied twice.
scale(3, factor: 4)    // Error: factor is an internal name.

func configure(! mode: i32 = 0, count: i32 = 1) => ()
configure(count: 3) // Valid; use the default for mode.
configure(3)        // Error: mode requires its name; do not skip to count.

func pair(first: i32 = 1, second: i32 = 2) => ()
pair(3)            // first = 3, second = 2.
pair(second: 3)    // first = 1, second = 3.
```

## 10.2. Common adaptation at expected types

This section owns the implicit adaptation of a value to a position whose expected Type is fixed: arguments, annotated initializers, assignment sources, by-value results, aggregate elements, enum payloads, defaults, and the sources of a fixed Target Result Type (arms, `yield`, `exit` and single-item bodies, §14.9.1). The expected Type comes from the declaration, the structure or ordinary common-Type inference; it is never derived backward from a borrow or a value read. §14.9.1 finds the common Type of result sources, including sources that differ only in reference layers over one read Type (§3.5.3); sources without a common Type, such as `ref/Node` and `Node`, need an annotation, or an explicit `@follow` when `Node` is Copy. The call form, explicit Type arguments and aliases do not change these rules. Positions without an expected Type use bare acquisition (§3.5), and Place results follow §7.1.1.

In the table, `U` is the complete Type named in the Expected column; the shared-reference and value-read rows may reach it through several input layers, including qualifying pair layers (§3.4.1) with the dependencies of §13.5.5.1.

| Input | Expected | Operation |
| --- | --- | --- |
| Readable Place storing `U` | `ref/U` | New shared borrow |
| Owner `U` temporary | `ref/U` | Materialize once and shared-borrow |
| Safe value-reference layers ending in `U` | `ref/U` | One shared reference to `U` (below) |
| `uniq/U` | `uniq/U` | Exclusive Reborrow for the required extent |
| Readable owning object handle `obj/U`, `rc/U` or `arc/U` | `objref/U` | Shared object borrow; no reference-count change |
| `objuniq/U` | `objuniq/U` or `objref/U` | The corresponding object Reborrow |
| Safe value-reference layers ending in `U` of a read Type | `U` | Value read (§3.5.3) |
| Function Item or concrete Closure (value or Place) | Common Function Type `U` | Erasure (§7.6.4): the source is acquired as the last row acquires it, then owned by a new `U` |
| Any other value or Place | Its complete Type | Bare acquisition, explicit transfer, or transfer of a temporary (§3.5) |

Exactly one table operation is selected; the required Origin relations of the fit are judged after selection ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-constraints)). The erasure row applies only at a position whose expected Type this section fixes: the common-Type search of §14.9.1 compares the sources' own Types and never selects `U` through it. Adaptations are never chained; the shared-reference and value-read rows each count as one operation however many layers they follow. The table is applied to the input's original complete Type: the Reborrow that bare acquisition gives a stored exclusive reference (§3.5) is never performed first and then adapted, so a stored `uniq/U` at an expected `ref/U` takes the shared-reference row directly. A same-Type temporary is transferred as is. The shared-reference and Reborrow rows operate on the existing reference values and add no dependency on a temporary slot holding them.

**One shared reference through layers.** An input of safe reference layers ending in `U` yields one `ref/U`. When a `ref` layer exists, the innermost `ref` layer is Copied with its own Origin, each `uniq` layer below it is Reborrowed as shared, and the result's Origin is the meet of those layers' Origins. The layers above the innermost `ref` layer add no dependency: a shared reference is Copy, and reading it only requires them to be valid at that moment. Without a `ref` layer, the result is a shared Reborrow through every layer, with the meet of all their Origins. Thus a single `ref/U` is Copied with permitted Origin shortening, and a single `uniq/U` is shared-Reborrowed. Every layer is checked for initialization, capability and Loans, as for the value read.

```kimi
func validate(node: ref/Node) -> () => ()
func report(found: ref/Node? during a)
    match found                  // A bare Place: Shared.
        .Some(let hit) => validate(hit) // hit: ref/(ref/Node); the inner reference is Copied.
        .None => ()

var first = Node.init()
var second = Node.init()
let nodes: [2 of uniq/Node] = [first@uniq, second@uniq]
for node in nodes                // node: ref/(uniq/Node)
    validate(node)               // Shared Reborrow through both layers.
```

There is no implicit borrow of a reference or handle slot, no implicit payload follow of an object, no implicit `rc`/`arc` strong duplication or exclusive borrow of a temporary, and no implicit object upcast, numeric (including integer/float) or user conversion. Apart from a Place storing an exclusive reference, which bare acquisition Reborrows (§3.5), a bare Non-Copy or Copy-unproven Place cannot be acquired by value, and a candidate that needs that Copy is inapplicable (§10.1). An explicit `@move` executes first and is not corrected by a later adaptation, so a transferred reference is passed to a same-Type expectation as that value.

A new exclusive borrow of an owned Place requires `@uniq`/`@objuniq`; only a Receiver Expression acquires one implicitly ([§7.3](07-functions-and-callable-values.md#73-explicit-receivers)). An annotation, assignment or result never adds lifetime or capability. A bare owned Place is therefore never applicable to a `uniq`/`objuniq` parameter, and implicit exclusive borrowing never switches candidates. Likewise, a bare Place's by-value acquisition and a new shared borrow of the same Place never choose between candidates: the parameter acquisition shape of §7.3.1 keeps a by-value parameter and a borrowing parameter of overlapping Types out of one position, so the acquisition follows from the Name and the position alone.

```kimi
func work(node: uniq/Node)
    validate(node)       // validate: ref/Node. Shared Reborrow.
    normalize(node)      // normalize: uniq/Node. Exclusive Reborrow.
    let transferred = node@move // The reference value itself.

var node = Node.init()
validate(node)           // Shared borrow of the owned value.
normalize(node@uniq)     // A new exclusive borrow is explicit.
let view: ref/Node = node // The same shared borrow at an annotated initializer.

func positiveOrZero(value: ref/Option<i32>) -> i32
    return match value
        .Some(let number) if number > 0 => number // ref/i32; a value read at i32.
        _ => 0
```

**Temporaries.** An owner temporary may be borrowed at every value position, not only at arguments. It is evaluated once and materialized in a Temporary Place with the ordinary temporary lifetime (§3.6.2), which lasts at least through a call; that lifetime is neither shortened nor extended to keep a borrow valid. Using `view` after `let view: ref/Node = makeNode()`, or returning that borrow, is rejected with a diagnostic naming the expired temporary and the use. A borrow that ends within the full expression, such as inside an aggregate literal passed to a call, passes the same lifetime check. A known source Type infers `U`; an unfitted literal follows **Literals** below.

**Literals.** An untyped integer literal, or a [literal-only expression](12-expressions.md#1231-type-inference), fits any representable candidate integer Type directly; floating literals follow the numeric rules. Literals are not defaulted to `i32`/`f64` before fitting, narrower widths are not preferred, and defaults never break overload ambiguity. Generic inference processes receiver, other-argument and known-result constraints first, then candidate fitting, and applies the ordinary value defaults last. Outside candidate comparison, an independent expression without an expected Type uses the ordinary numeric defaults. `null`, empty collections and untyped functions gain no universal fallback Type.

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(1)      // Error: both integer Types fit.
let x = 1      // Independently defaults to i32.
choose(x)      // Exact i32.
choose(1@i64)  // Exact i64.

func inspect<T>(value: ref/T) -> () => ()
func makeValue() -> i64 => 1
inspect(makeValue()) // Infer T = i64 and borrow the temporary.
inspect(1) // T defaults to i32 after constraints.

func chooseBorrow(value: ref/i32) -> () => ()
func chooseBorrow(value: ref/i64) -> () => ()
chooseBorrow(1) // Error: both fit; default i32 does not break the tie.
```

### 10.2.1. Inference and adaptation classes

Type inference and acquisition planning are separate: inferring a Type performs no Copy, Move or borrow.

1. Bind explicit Type arguments, then infer the remaining Type arguments from the original complete Types of the receiver and the arguments. A bare `T` binds the original Type, except that a parameter whose Type is a read Type under the declaration's Constraints (§3.5.3), that is, a `T` whose Constraints imply `Position`, `PositionRange` or `PrimitiveInteger`, or `Wrapping<T>` under `T is PrimitiveInteger`, binds, from an argument of safe reference layers, the terminal referent Type: only value Types are read Types, so no other binding exists, and the argument is then value-read. Thus `ref/Wrapping<u32>` binds `T = u32` for a `Wrapping<T>` parameter. A declared `ref/T` or `uniq/T` binds `T` to an owned argument's complete Type or to the immediate referent of a borrow value's outermost layer, and the rows of the table that follow several layers apply only after `T` is fixed. A [pair layer](13-operators-and-assignment.md#pair-layers) is not a borrow value here: `T` binds to its complete Type `W`, so `inspect(c[i])` for an element Type `s/U` binds `s/U` and shared-borrows the element slot as `c[i]@ref` does, while `inspect(c[i]@follow)` binds `U`. Constraints over several arguments are solved together, and no argument fixes `T` first by traversal order.
2. An independently known expected result Type fills still-unbound parts by ordinary Type matching; apart from the exception of step 1, it never selects an unknown Type by inverting a borrow or value read, and never changes an established Type. Origin inference and shortening follow the ordinary rules, keeping bound internal Origins.
3. Normalize the parameter Types and plan the acquisition of each argument under §3.5 and this section. An existing reference passed to a borrow parameter is Reborrowed in a generic call as elsewhere; consuming the reference value itself needs `@move`.

```kimi
// n: ref/i32; r: uniq/Node; identity<T>(value: T) -> T returns value@move.
let a = identity(n)        // T = ref/i32.
let b = identity<i32>(n)   // Value read at the fixed i32.
let c = identity(r)        // T = uniq/Node; the argument is an exclusive Reborrow.
// While c is live, conflicting uses of r are rejected.
let d = identity(r@move)   // After c's last use: the reference value is transferred and r is Moved.

// double<T>(value: T) -> T with T is PrimitiveInteger; values.remove<P>(index: P) with P is Position.
let e = double(n)          // T = i32; n is value-read.
values.remove(n)           // P = i32.
```

An unannotated local initializer is bare acquisition (§3.5). Adding or removing an annotation of the same complete Type and Origin conditions therefore changes no acquisition, Loan or cleanup: for `r: uniq/Node`, `let saved = r` and `let saved: uniq/Node = r` both Reborrow, and `let saved = r@move` transfers. An annotation that requires a shared reference or a value read selects that adaptation instead, such as `let view: ref/Node = r`. Omitting the result of a named function is not such an annotation: the function returns Unit (§7.1).

```kimi
func validate(node: ref/Node) => ()
func normalize(node: uniq/Node) => ()

func work(r: uniq/Node)
    validate(r)               // A shared Reborrow.
    normalize(r)              // An exclusive Reborrow in the same Semantics.
    let saved = r             // The same Reborrow as let saved: uniq/Node = r.
    normalize(saved)
```

Candidates are checked for applicability with their acquisition plans, and the selected plan acquires each argument once. The adaptation classes are compared in this order, best first, with no further preference within a class:

| Class | Meaning |
| --- | --- |
| Exact | Normalized Type compatibility requiring no adaptation operation, including no borrow or Reborrow |
| Literal fitting | Directly fitting an unresolved literal or literal-only expression to the candidate's Type |
| Same-Semantics Reborrow | A Reborrow that preserves the input's outer Semantics |
| Cross-Semantics adaptation | Any other borrow or Reborrow of the table, one shared reference through more than one layer, or a value read |

Exact describes Type adaptation, not value transfer: an Exact by-value argument still Copies when bare or transfers under `@move`, and Copy versus transfer adds no ranking preference. A required exclusive Reborrow is not Exact even when the written Types match. Type and declaration permissions and the Place-versus-temporary form are checked during applicability; flow-dependent initialization and active Loan conflicts are checked after selection (§10.6). Selected exclusive adaptations use [call reservation and activation](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations).

Explicit transfer, Take and the Copy proof of a bare by-value acquisition are part of applicability, so adding a candidate never introduces an implicit Move or borrow (§7.3.1). A generic body verifies acquisition, adaptation and candidate ranking symbolically from its published Constraints and rejects at definition what it cannot prove; instantiation neither reselects candidates nor redistributes Copy and Move. Pattern binding Types are unaffected.

[Inherited receiver projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection) and the complete Sealed payload follow of a receiver (§12.4.4) are member-receiver operations supplied by implicit receiver acquisition (§7.3); they do not apply to ordinary arguments or unbound calls and take no part in candidate comparison. The erasure row of §10.2 ([common function conversion](07-functions-and-callable-values.md#764-function-references-and-common-type-conversion)) belongs to no class of this table: in step 1 of §10.4, two erasures at one argument are equal, and an erasure and any other row are incomparable.

## 10.3. Expected results

This section is the static expected-result judgment of [the relation table](03-types-and-values.md#38-type-relations-and-expression-operations): a known result is filtered by the acquisition or common adaptation its use position admits (§10.2), without ranking candidates.

An expected result may complete inference. It excludes a candidate only when the use position admits no acquisition or adaptation of that candidate's known result (**Result adaptation**, below). Origins are instantiated, but their relations, including permitted covariant shortening, never exclude a candidate: applicability (§10.1 steps 5–6) and result filtering use only the structural part of each fit, and its required Origin relations are judged after selection ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-constraints)). Constraints still take part in applicability, including those whose proof involves Origins: `Owned`, `Callable` and Type identity (§8.7). No numeric or user conversion is inserted to keep a candidate, and a function body's literal is never retyped to change its established return Type.

Expected results never rank candidates, including by the amount of result adaptation. Result Loan/Origin propagation and Copy/Move still apply. An expectation comes from a surrounding annotation, a fixed parameter or a declared result, subject to §10.5; it cannot circularly select its own source candidate. Discarding a call supplies no expected Unit Type, even in a construct whose Target Result Type is fixed as Unit (§14.2).

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // Selects fetch(i32) by result compatibility.
fetch(1)                   // Error: discarding leaves both candidates.
```

These declarations have distinct parameter Signatures; declarations that differ only in return Type or result mode (§7.1.1) are invalid regardless of call-site expectations.

**Result adaptation.** Once a candidate's published result Type and result category are fixed, the acquisition or adaptation that the use position admits is part of its applicability. The same procedure applies to ordinary values, Place results and value reads:

| Use position | Rule applied to the known result |
| --- | --- |
| Value position or result source with a fixed expected Type | The common adaptation of §10.2, with the position's own lifetime check |
| Value position without an expected Type | Bare acquisition (§3.5) |
| Position that requires a Place | The Place's Type, capabilities and Origin are checked; an ordinary value is never materialized into a Place result |
| Explicit operation or discard | That operation's own rule; discarding supplies no Unit expectation |

This check uses fixed declaration information only. An unknown result `T` is never inferred backward from the adapted Type, a Place's published Type is never inferred from a borrow adaptation, and Origins and Loans follow the acquisition actually selected. A candidate returning `T` therefore cannot survive an expected `ref/T` by borrowing a temporary result outside the lifetime rules of §10.2, and an outer `@ref` never prefers a Place-returning candidate.

```kimi
func read(x: i32) -> ref/i32
func read(x: i64) -> i32
// let value: i32 = read(1)   // Error: both results fit through a value read or exactly.
let value: i32 = read(1@i32)  // The argument selects; the result is then read.
```

## 10.4. Best candidate

Pairwise comparison yields better, worse, equivalent or incomparable. **Only equivalent candidates proceed to the next step.** A candidate is selected only if it is better than every other applicable candidate:

1. Compare adaptation quality for each explicit source argument. The receiver is excluded: its acquisition is common to the function group, because every function with a receiver in the group shares one receiver shape ([§7.3](07-functions-and-callable-values.md#73-explicit-receivers)). A dominates B only if it is no worse everywhere and better somewhere; all-equal proceeds, and opposing advantages are incomparable. Named arguments are matched by the same source expression, not by candidate parameter order. Defaults are excluded, and numeric costs are never summed.
2. Compare substituted parameter Types at the same positions. A dominates B if every Type is equal or a defined subtype and at least one is a strict subtype; all-equal proceeds, and unrelated Types or opposing subtype advantages are incomparable.
3. Prefer a function with no generic parameters of its own, including length parameters. A generic enclosing Type alone does not make the function generic.
4. Prefer fewer defaults used by this call.
5. Otherwise, report ambiguity.

Numeric Types are not ranked by width, Constraints not by strength or clause count, and generic declarations not by general pattern partial ordering. `uniq/T <: ref/T` is never invented from the ability to reborrow. Incomparability at an earlier step cannot be rescued by nongeneric status or fewer defaults, and declaration, file, alias and name order never break ties.

```kimi
func inspect(value: uniq/(uniq/Node)) -> () => ()
func inspect(value: ref/Node) -> () => ()   // Value(uniq/Node) and Value(Node) do not overlap (§7.3.1).

func work(x: uniq/(uniq/Node))
    inspect(x)          // The first candidate: a same-Semantics Reborrow beats one shared reference through two layers.
    inspect(x@follow)   // The second: the selected Place stores uniq/Node, which only the shared candidate adapts.
let action: (ref/Node) -> () = inspect
```

The first candidate wins for the two-layer input because its Semantics match, not because exclusivity is stronger. `f<T>(T)` and `f<U>(Box<U>)` remain tied for `Box<i32>` when substitution makes both parameter Types equal and the later steps tie.

A bare Place reached through an exclusive reference is also never applicable to an exclusive candidate; `@uniq` selects that candidate (§15.1.5). Field Move eligibility and its generic limits follow [Field Move](11-properties.md#1112-move-paths-and-inherited-fields).

```kimi
func bumped(score: Score) -> Score   // A by-value variant and an exclusive variant need different names (§7.3.1).
func bump(score: uniq/Score) -> ()

struct Game
    var score: Score
    func play(self: uniq/Self)
        bump(self.score@uniq)        // The exclusive borrow is written.
        // bump(self.score)          // Error: the only candidate needs @uniq; no by-value candidate takes the call and discards its update.
```

## 10.5. Inference boundaries and specialization

Local Types are fixed at their declaration, and later uses cannot infer them backward. [Named functions](07-functions-and-callable-values.md#7-functions-and-callable-values), including local functions, methods and Contract requirements, have an explicit result or Unit, independent of access and body form; neither bodies nor callers infer their signatures. [API accessibility](09-names-signatures-and-access.md#932-api-signature-accessibility) is checked, and substituting a written generic result does not reanalyze the body. Local binding inference and [anonymous-function inference](07-functions-and-callable-values.md#761-syntax-and-inference) remain available.

Explicit Type arguments follow the [slot rules](08-generics-constraints-and-contracts.md#81-generic-type-parameters). Explicit specializations take no part in inference or overload applicability; an implementation is selected only after the original declaration and its static generic arguments are determined.

Generic inference and substitution use the [complete-Type slots and projections](08-generics-constraints-and-contracts.md#81-generic-type-parameters) and preserve all bound Origins and inferred Loan requirements, including nested dependencies. A surrounding borrow such as `ref/T` keeps `T`'s internal dependencies alongside its own Origin and Loan. Substitution alone creates no Borrow or Reborrow, releases no Loan and extends no lifetime; actual call-site checks follow the [ownership and Origin rules](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis).

**Try boundary.** A `try` operand is resolved in Value Context with no expected Type (§17.2.4). Neither the success expectation nor the return target feeds operand inference or retries its overload selection. Explicit discard also supplies no expectation (§14.2.4).

**Nested calls.** Bidirectional checking supports literals, function references, anonymous functions and expressions directly checkable against a candidate Type. It never searches combinations by rerunning an inner overload resolution for every outer candidate in `f(g(x))`:

- Independently typable arguments and explicit Type information are processed first.
- An inner expression may use an expected Type shared by all remaining outer candidates.
- If one outer candidate is already determined, its expectation is used, and eliminated candidates are not revived after a failure.
- Otherwise an annotation, explicit Type arguments or a typed intermediate binding is required.

```kimi
func inner(value: i32) -> i32 => value
func inner(value: i64) -> i64 => value
func outer(value: i32) -> () => ()
func outer(value: i64) -> () => ()
outer(inner(1))                    // Error: would require nested search.
let intermediate: i64 = inner(1)  // Expected result fixes the inner call.
outer(intermediate)
```

**Call signatures.** The **known call signature** of a callable Type is the signature with which a value of that Type is called directly (§7.6.3, §8.6), and an argument's known call signature is that of its Type:

| Type | Known call signature |
| --- | --- |
| Function Item Type | The declaration's signature with its bound arguments substituted |
| Concrete Closure Type | Its internal call signature |
| Common Function Type | Its structure |
| Generic parameter `G` | The one signature that the `Callable` Constraints on `G` give, when they give one (§8.6) |

An anonymous function argument supplies only the parameter Types and result Type written in its header, as the corresponding parts of its known call signature; an omitted part supplies nothing.

A position has a **fixed expected call signature** `S` in two cases. When its expected Type (§10.2) is a common Function Type, `S` is that Type. When its expected Type is `F`, `ref/F` or `uniq/F` for an unbound slot `F` of the outer candidate, `S` is the one signature common to all `Callable` Constraints on `F`, whatever their receivers; if their signatures differ, there is no `S`. While several outer candidates remain, only an `S` common to all of them is used, as for nested calls. As evidence, the slots in `S` are unknowns of the matching of §10.8; as an expectation, only the parts of `S` that contain no unbound slot of the outer candidate guide the argument.

A **waiting argument** is an argument checked only against an expectation from the call: an anonymous body, a nested call, or a function reference that needs a fixed expected call signature.

**Function references.** The candidates of a function reference are the declarations of its committed function group that pass the explicit Type-argument check of §10.1 step 1. A Type-qualified instance function reference is the unbound form of §7.3: its `self` is an ordinary parameter at its declared position. A reference needs `S` when two or more candidates remain or when a candidate has own slots that explicit Type arguments do not bind. A reference that does not need `S` is an independently typable argument, and its known call signature is evidence under §10.8. A reference at a position without `S` is a value only when exactly one candidate remains and that candidate has no own slots or has all of them bound explicitly, so an unresolved overload set is not a value.

A reference that needs `S` is selected once every parameter Type of `S` is closed; the result Type of `S` may stay open:

1. Each candidate's own slots are bound by matching its parameter Types against those of `S`, and its result Type against only the closed parts of the result of `S`, under §10.8 and without the adaptations of §10.2.
2. A candidate is applicable when all its own slots are bound, its Constraints and conditional-member premises are Proven (§10.1 step 5), and its substituted signature is compatible with the closed parts of `S` under §10.7, which includes equal arity.
3. Applicable candidates are compared under §10.4 unchanged. A reference has no source arguments and uses no defaults, so steps 1 and 4 always tie, and results never rank candidates.

After selection, the prohibition on unsafe functions and `drop` (§7.5, §7.6.4) and the erasure conditions (§7.6.4) are checked; a failure of either is an error and never selects another candidate. At an expected `F`, `ref/F` or `uniq/F`, `F` binds to the selected Function Item Type without erasure; the reference is a temporary, so at `ref/F` it is materialized and borrowed shared (§10.2). A reference that needed `S` is a waiting argument: the selected Item's signature is used for checking but never fills the open result of `S`. Function values have no labels or defaults. A conformance failure cannot change the chosen overload or capture mode.

**Requirement references.** A requirement referenced through a constrained Type, such as `T.compare` under `T is Comparable`, is the Function Item of the requirement declaration instantiated with the binding of `Self`. Its signature is the requirement's, with `Self` and the associated Types substituted, and its calls reach the implementation as §8.4.6 states. Instantiation never replaces it with the implementation's own Function Item (§10.2.1). `Comparable.compare` identifies no implementation and is invalid as a reference too (§8.4.6).

```kimi
func show(value: i32) -> () => ()
func show<T>(value: T) -> () => ()
func identity<T>(value: T) -> T => value@move
func inspect<T>(value: ref/T) -> () => ()

func invoke<F>(action: ref/F) -> ()
    F is Callable<(i32) -> ()>
    action(1)

let a: (i32) -> () = show         // show(i32): §10.4 step 3 prefers the non-generic function.
invoke(show)                      // The same selection; F is its Function Item Type, and the temporary is borrowed.
let b: (i32) -> i32 = identity    // T = i32 from S; then erased.
let c: (ref/Node) -> () = inspect // T = Node; the per-call Origin stays outside T.
let d = show<i32>                 // No S: one candidate remains after the explicit arguments.
// let e = show                   // Error: no S and two candidates.
// let f = identity               // Error: no S and T is unbound.
// let g: (ref/Node) -> ref/Node = identity // Error: T would hold a per-call Origin.
```

**Anonymous body context.** Explicit Types, independently typable arguments and generic constraints are processed before the body is checked, independently of argument order. Arity and explicit Types may filter candidates, and the Types written in the header are evidence as parts of the anonymous function's known call signature (§10.8). The written signature, a common remaining expectation, or an already selected candidate's signature is used; the fixed expected call signature guides the omitted parameter Types while `F` keeps its concrete Closure Type. If unresolved candidates cannot provide the needed expectation, an annotation is required. Return expressions are never inspected to select candidates, bodies and captures are never retried across candidates, and parameters are never inferred from later uses.

Waiting arguments see only the candidates that remain applicable (§10.1); their bodies and results never select a candidate and are never evidence for an outer slot (§10.8).

If the normalized return Type is Unit before the body is checked, the single-item expression is discarded (§7.1); otherwise it is in Value Context, including for return inference. That return inference infers the anonymous function's own result only; it is never evidence for an outer slot (§10.8). Unit inferred from another argument before body checking is allowed, whatever its spelling or source. The body and captures are not reinterpreted after later Unit inference or generic instantiation. A standalone lambda without an expectation can infer its return, while an already typed function value cannot erase its return to Unit. There is no overload preference between using and discarding lambda results.

~~~kimi
func run(action: () -> ()) => action()
func run(action: () -> i32) => action()
run(func () => compute()) // Error: differing expectations; compute returns i32.
run(func ()
    return compute()
) // Same error: an explicit return does not select an expected signature.
run(func () -> i32 => compute()) // Explicit return Type selects the second overload.
func apply<U>(value: U, action: () -> U) -> U => action()
apply((), func () => compute()) // U is Unit before checking the lambda; discard its value.
// func make<T>() -> T => 123 is invalid for arbitrary T; instantiation cannot rescue it.
~~~

**Constraints after inference.** The [limited proof system](08-generics-constraints-and-contracts.md#87-constraint-proof-system) applies: Proven satisfies a requirement, Refuted rejects it, and Error diagnoses invalid or contradictory evidence. Unknown may keep only a legitimate dependency that resolves by its deadline; it proves neither applicability nor negation. Generic capabilities must be proven at definition acceptance (§8.10). Nondependent Names bind at the definition and use the declared Constraints; failing to prove `T is C` does not prove `T is not C`. Deferred members keep the [definition environment](18-modules-and-dependencies.md#18-modules-and-dependencies), never caller imports. All necessary Constraints are proven before concrete finalization. Arbitrary theorem proving, Type enumeration and constraint-strength ranking are not allowed.

Environment-selected membership follows §19.4: excluded declarations neither merge nor enter candidate sets. Conditional-conformance members (§8.4.8) and conditional members (§7.4) remain in ordinary lookup; their published conditions affect applicability, not lookup stopping or syntax selection. Each defining generic environment is preserved.

## 10.6. Usage legality and operators

After selection, unsafe permission, initialization and Move state, actual Loans and lifetimes, required accessor access, write capability, and other control-flow or ownership conditions are checked. Static Type and declaration permissions needed for adaptation were checked earlier; flow-dependent failures never change the selected overload.

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: i64) -> () => ()
let number: i32 = 1
inspect(number) // Select Exact i32, then reject without an Unsafe Block.
```

Adding a better overload can invalidate existing calls, even if the new overload then fails usage legality. A failed Move, Loan or Property permission check cannot retry with a borrow, a getter or a same-name declaration.

Operator operands keep the fixed syntax, evaluation order and permitted adaptations. Built-in operations and the comparison Contract mappings of §13.4.1 define the available operator candidates, and the [Indexable Contracts](04-arrays-indexing-and-slices.md#469-indexable-contracts) define index-expression candidates; there are no extension operator candidates in this revision. User-defined arithmetic and additional ambiguity-resolution syntax remain deferred, and ordinary lookup must not invent them (§10.9).

**Diagnostics** distinguish undefined, wrong-role and inaccessible names, path or value-kind conflicts, missing receivers and inherited declarations named without a qualifier (§9.4), Type-argument or argument mismatches, no applicable overload, ambiguity, a gathered group that violates the parameter acquisition shape (§7.3.1), inference boundaries, dependency cycles, declaration errors and usage errors. Ambiguity diagnostics show the candidate Signatures and declaration locations and keep useful rejection reasons without dumping every tentative error.

For an ambiguous function reference (§10.5), the diagnostic shows the fitting candidates with their substituted signatures and declaration locations, with Advice to write explicit Type arguments or a Type annotation. When a per-call Origin leaves a reference's slot unsolved, the diagnostic shows that slot and the corresponding parameter of the fixed expected call signature (§10.5), with Advice to wrap the reference in an anonymous function. When a required slot stays unsolved and appears in the fixed expected call signature of a waiting argument, the inference-boundary diagnostic carries Advice to write that argument's Types, namely an anonymous function's parameter or result Types or a function reference's explicit Type arguments, or to annotate the Type of the call's result. None of these diagnostics offers a repair candidate (§23.3.6.9).

A call whose gathered group violates the parameter acquisition shape is reported at the called Name as §7.3.1 states; it fixes no callee, acquisition plan or result, explicit operations and inner calls already inside its arguments are not undone, independent problems stay visible, and problems that depend on the call, including an expectation it could not supply, follow the prerequisite rules of §23.3.6.4. Exhausting a resource limit is distinct from language ambiguity or mismatch; it must request annotations or smaller expressions and never chooses the first candidate.

## 10.7. Callable signature compatibility

Callable variance uses the static Type relations of [the relation table](03-types-and-values.md#38-type-relations-and-expression-operations). Common Function Type conversion and receiver acquisition are separate operations, and receiver adaptation adds no argument conversions.

Common Function Type conversion and `Callable<r, S>` use one rule. For implementation `(A1, ..., An) -> R` and required `(P1, ..., Pn) -> Q`, the arity must be equal, `Pi <: Ai` must hold at every position, and `R <: Q` must hold. Here `<:` means normalized complete-Type identity or an already defined subtype relation, keeping Semantics and Origins. Parameter labels, name-omission permissions and defaults cannot bridge a mismatch.

Compare parameter/result Origins and Loans for every admitted call using [canonical Origin contracts](15-ownership-and-lifetime-analysis.md#1537-canonical-contracts-and-verification). Required quantifiers are rigid; only the implementation's call-time Origins are inferred. Fixed captures cannot become fresh quantifiers. Compatibility inserts no Borrow/Reborrow, follow, numeric or user conversion, or argument/result erasure, and never reinfers committed Types. Covariant Origin shortening remains valid, while exclusive Core invariance and result Loans remain mandatory.

Implicit erasure applies only after the expected common Function Type is fixed. No overload ordering is defined between a concrete direct match and an erasure: they are incomparable (§10.2.1), so use an annotation or typed intermediate where that comparison would be needed. Allocation cost never ranks candidates, and a receiver, Copy, Owned or Loan failure cannot reopen selection.

## 10.8. Generic argument inference

The additional [fixed-array inference rules](04-arrays-indexing-and-slices.md#44-function-length-parameters) apply to lengths and literal element counts. Lengths are kept alongside Type, Semantics and Origin bindings within each candidate.

Within each candidate, explicit arguments bind first. The remaining structural Type, Semantics and Origin constraints are collected together from the receiver and the independently typable arguments under §10.2.1, never by fixing the first input and adapting later inputs to it.

In the same fixed point as these constraints, each parameter with a fixed expected call signature `S` (§10.5) matches `S` against a known call signature. A common Function Type parameter matches it against the argument's known call signature, whether the argument is a Function Item, a concrete Closure, a common Function value or a value of a generic parameter. A parameter of Type `F`, `ref/F` or `uniq/F` under `F is Callable<r, S>` matches it against the known call signature of the Type that the argument itself binds to `F` (§10.2.1 step 1). In either case, an anonymous function argument supplies only the parts written in its header (§10.5). The matching covers the result shape (value or Place), the parameter Types and the result Type: structure and Semantics are matched by normalized identity and Origins by limited inference (§15.3.6), with no adaptation, subtype search or conversion search. A per-call Origin (§8.6) never becomes the solution of an Origin inside a slot; that part is left to other evidence or to §15.3.6. These matches are solved together with the argument Types: a failed match and conflicting evidence for one slot are both mismatches that make the candidate inapplicable, and no argument fixes a slot first by traversal order. Waiting arguments (§10.5) are never evidence: even when `F` is fixed as the concrete Closure of an anonymous function after its waiting body is checked, that Closure's result Type fills no outer slot.

```kimi
func consume<T, F>(action: F) -> T
    F is Callable<owner, () -> T>
    return action@move()

func both<T, F, G>(first: ref/F, second: ref/G) -> T
    F is Callable<() -> T>
    G is Callable<() -> T>
    return first()

func make<T>(action: () -> T) -> T => action()
func makeSmall() -> i32 => 1
func makeLarge() -> i64 => 2

let small = consume(makeSmall)             // T = i32 from the known call signature of makeSmall.
let large = make(makeLarge)                // T = i64 likewise; the argument is then erased.
let written = consume(func () -> i64 => 2) // T = i64 from the written result Type.
// let open = consume(func () => 2)        // Error: only the body could fix T; write its result Type or annotate open.
// let mixed = both(makeSmall, makeLarge)  // Error: conflicting evidence for T; neither argument fixes it first.
```

An independently known expected result fills only still-unbound parts. The nested-expression boundaries and literal fitting apply, and literal defaults are used only after all other evidence. An expected Type propagates through a Semantics-preserving adaptation to an untyped literal: under an expected `s/T`, the untyped literal or literal-only operand `n` of `n@s` is fitted to `T`. A typed operand keeps its own Type there, so the borrow forms from its Place and never from a value read of it.

An unbound `s` is inferred directly from the source's outer Semantics. No implicit Borrow, Reborrow or other conversion is searched to find a common Semantics: `owner` and `ref` evidence for the same `s` conflict. Once a target Type is fixed, including by explicit arguments, normal adaptation is checked separately. Origin inference uses the [limited principal-solution rules](15-ownership-and-lifetime-analysis.md#1536-limited-origin-inference).

Structural, Semantics and Origin constraints are solved to a fixed point, and every required slot must be resolved uniquely and consistently. An occurs-check rejects infinite substitutions such as `X = ref/X`; nominal recursive Types instead require a valid layout. Cycles supply no result. Unresolved work is kept only for an identified dependency that can resolve by its deadline.

Argument or candidate traversal order, arbitrary conversion chains, common-base search and Constraint strength never choose a solution. Constraint substitution uses the same binding table as Type expressions: `s is reference` checks the Semantics projection, and the pair's `T is C` checks its target projection and that requirement's role; an ordinary `T` keeps its complete Semantics. Then fixed-target argument adaptation, substituted Constraints and expected-result compatibility are checked; flow-dependent acquisition, Loans and cleanup follow selection.

## 10.9. Inference and operation design boundaries

The following are not specified in this revision, and implementations must not invent them through broader search:

- General Const arguments beyond [function lengths](04-arrays-indexing-and-slices.md#44-function-length-parameters), standalone Semantics slots, partial, default or variadic generic arguments, and partial or conditional specialization.
- Implicit argument or receiver adaptations beyond the common adaptation table, and exact contextual-binding boundaries for additional accessor or function forms. The explicit Borrow table adds no implicit overload preferences.
- Operator candidate collection and explicit selection syntax beyond the built-in operators, the comparison mappings and the Indexable Contracts. Constructor collection is defined under [constructors](06-declarations-and-containers.md#623-constructors); external/internal parameter names and the `!` boundary are defined in §7.2.
