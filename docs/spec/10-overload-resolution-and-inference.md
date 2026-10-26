# 10. Overload resolution and inference

[Specification index](../SPEC.md)

This chapter selects the declaration that a call, function reference or construction uses and infers its own Type, Semantics, length and Origin arguments. Container qualifiers are resolved first ([§9.7.1](09-names-signatures-and-access.md#971-bound-container-paths)); an omitted outer Container environment is never inferred, and a later failure never reopens qualifier lookup. Argument adaptation, expected-result compatibility and acquisition legality are separate judgments ([§3.8](03-types-and-values.md#38-type-relations-and-operations)). Associated-Type inference ([§8.5.5](08-generics-constraints-and-contracts.md#855-inference)) does not change the rules of this chapter.

## 10.1. Candidate applicability

Resolution first checks that the committed function group satisfies the parameter acquisition shape ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)). Each declaration is then checked:

1. Check the count and kinds of the explicit Type arguments.
2. Match arguments to parameters and record the defaults used ([§10.1.1](#1011-argument-matching)).
3. Infer Type arguments from the receiver and the arguments, including call-signature matching ([§10.8](#108-generic-argument-inference)).
4. Let an independently known expected result Type fill the remaining Type arguments, without changing fixed ones.
5. After substitution, check the argument adaptations ([§10.2](#102-common-adaptation)), including the Copy proof a bare Place's by-value acquisition needs ([§3.5](03-types-and-values.md#35-copy-and-move)), the Constraints and any conditional-member premises ([§7.5](07-functions-and-callable-values.md#75-function-constraints), [§8.8](08-generics-constraints-and-contracts.md#88-conditional-conformance)).
6. With an expected result, reject a candidate whose result the use position cannot admit ([§10.3](#103-expected-results)).

Waiting arguments ([§10.5.1](#1051-argument-kinds)) are not checked per candidate; they are checked as that section states.

**Unknown applicability.** A Constraint or premise that is Unknown at step 5 leaves the candidate's applicability unproven. Unproven candidates are compared as though applicable. If one candidate is strictly best ([§10.4](#104-best-candidate)), it is selected; each of its Unknown judgments becomes an obligation checked after selection, and it is an error unless it is Proven under Finality ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)). If no candidate is strictly best, the call is ambiguous (error). Selection is never deferred or retried, and an unproven candidate is never dropped so that another wins.

No applicable candidate is an error. Checking a candidate performs no Copy, Move, borrow or default evaluation. A declaration error stays an error even when another candidate succeeds. The acquisition mode of a bare Place argument never depends on which candidates remain ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)). Callable effect bounds neither filter nor rank candidates; the selected call or function reference discharges them ([§8.11.4](08-generics-constraints-and-contracts.md#8114-callable-effect-bounds)), and a failure never reselects.

```kimi
func keep<T>(value: T) -> ()
    T is Owned
func keep<T>(value: T, tag: i32 = 0) -> () => ()
func store(view: ref/i32)
    keep(view)     // The first is strictly best (fewer defaults); Error: T is Owned is not proven.
    keep(view, 1)  // The second: the only candidate with two parameters.
```

### 10.1.1. Argument matching

- A call writes the task argument first exactly when its callee has a task slot ([§24.2.2](24-suspension-and-asynchronous-tasks.md#2422-passing)); otherwise it is an error. The task argument binds no parameter.
- Positional arguments precede named arguments and bind parameters in declaration order, only before the `!` boundary of a direct call ([§7.2.1](07-functions-and-callable-values.md#721-argument-name-boundary)). Positional matching never skips a name-required parameter, a parameter with a default or one supplied later by name, and never searches by Type. A method call ([§7.3.3](07-functions-and-callable-values.md#733-method-calls)) first removes its receiver position.
- Named arguments use external names, may be reordered and bind a parameter at most once. Unknown labels and excess positional arguments are errors.
- An unbound parameter takes its default, or the call is an error. Defaults supply no inference evidence.
- Explicit arguments are evaluated in source order, then the defaults in parameter order ([§7.2.3](07-functions-and-callable-values.md#723-default-evaluation)). A function-value call passes all arguments positionally.

```kimi
func scale(value: i32 ! by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // by is evaluated before value.

func configure(! mode: i32 = 0, count: i32 = 1) => ()
configure(count: 3)    // mode takes its default.
configure(3)           // Error: mode requires its name; matching does not skip to count.
```

## 10.2. Common adaptation

Common adaptation fits a value to a fixed expected Type: at arguments, annotated initializers, assignment sources, by-value results, aggregate elements, enum payloads, defaults and the sources of a fixed Target Result Type ([§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type)). The expected Type is never derived backward from a borrow or a value read. Positions without one use bare acquisition ([§3.5](03-types-and-values.md#35-copy-and-move)).

`U` is the complete expected Type. The shared-reference and value-read rows may follow several layers, including qualifying pair layers ([§3.4.3](03-types-and-values.md#343-reference-path-selection), [§13.5.5](13-operators-and-assignment.md#1355-follow)).

| Input | Expected | Operation |
| --- | --- | --- |
| Readable Place storing `U`, except a reference or handle slot | `ref/U` | New shared borrow |
| Owner `U` temporary | `ref/U` | Materialize once and shared-borrow |
| Safe value-reference layers ending in `U` | `ref/U` | One shared reference to `U` |
| `uniq/U` | `uniq/U` | Exclusive Reborrow |
| Readable `obj/U`, `rc/U` or `arc/U` | `objref/U` | Shared object borrow; no count change |
| `objuniq/U` | `objuniq/U` or `objref/U` | Object Reborrow |
| Safe value-reference layers ending in a read Type `U` | `U` | Value read ([§3.5.3](03-types-and-values.md#353-value-read)) |
| Function Item or concrete Closure | Common Function Type `U` | Erasure ([§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)) |
| Any other value or Place | Its complete Type | Bare acquisition or transfer ([§3.5](03-types-and-values.md#35-copy-and-move)) |

A **reference or handle slot** is a Place whose Type is a reference (`ref`, `uniq`, `objref`, `objuniq`) or an owning object handle (`obj`, `rc`, `arc`). The first row never borrows it implicitly; write `clone(handle@ref)`. The other rows still adapt its referent or object, for example `obj/U` to `objref/U`. Other named Types, including `Weak<S>`, take the first row.

Explicit acquisition runs before implicit adaptation. `@move` transfers the value and responsibility and marks a source Place Moved; no later adaptation replaces or undoes that transfer. A transferred exclusive reference that directly fits the expected Type is passed unchanged, without Reborrow. Otherwise, select one row using the acquired value's complete Type and category; for a bare input, use its original Type and category. At most one implicit adaptation runs: a stored `uniq/U` at `ref/U` takes the shared-reference row directly. Explicit acquisition does not count as an implicit adaptation. The ordinary temporary lifetime, Loan and erasure rules apply, and Origin relations are judged after selection ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)).

**One shared reference through layers.** With a `ref` layer, the innermost `ref` is Copied, each `uniq` layer below it is shared-Reborrowed, and the Origin is the meet of those layers' Origins; outer layers add no dependency. Without one, the result is a shared Reborrow through every layer. Every layer is checked for initialization, capability and Loans. The shared-reference and Reborrow rows add no dependency on a temporary slot holding the reference.

**No other implicit operation.** There is no implicit object payload follow, `rc`/`arc` duplication, exclusive borrow of a temporary, object upcast, numeric or user conversion. A bare Non-Copy or Copy-unproven Place is never acquired by value, except that bare acquisition Reborrows a stored exclusive reference; a candidate that needs that Copy is inapplicable. A new exclusive borrow of an owned Place needs `@uniq` or `@objuniq`, except for a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-receivers)), so a bare owned Place, or one reached through an exclusive reference ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)), never fits a `uniq` or `objuniq` parameter. An annotation never adds lifetime or capability.

**Temporaries.** An owner temporary is evaluated once into a Temporary Place with the ordinary lifetime ([§3.6](03-types-and-values.md#36-temporaries)), which is never extended for a borrow: using `view` after `let view: ref/Node = makeNode()` is an error.

```kimi
func validate(node: ref/Node) -> () => ()
func normalize(node: uniq/Node) -> () => ()

func work(node: uniq/Node)
    validate(node)              // Shared Reborrow.
    normalize(node)             // Exclusive Reborrow.
    let transferred: uniq/Node = node@move // Transfers the reference; no Reborrow.

var node = Node.init()
validate(node)                  // Shared borrow of the owned value.
normalize(node@uniq)
// normalize(node)              // Error: no implicit exclusive borrow.
validate(node@move)             // Transfer, then borrow the temporary; node is Moved.
```

### 10.2.1. Literals

Literal arguments fit a candidate's Type under the fitting rules of [§12.3](12-expressions.md#123-literal-typing); position and range forms keep their own rules. Narrower widths are not preferred.

Literal defaults (`i32`, `f64`, and `isize` in length contexts; [§12.3](12-expressions.md#123-literal-typing)) are applied only after Best Candidate selection, and only to the selected candidate. During applicability and comparison, a slot whose only evidence is a Literal argument ([§10.5.1](#1051-argument-kinds)) stays unbound ([§10.8](#108-generic-argument-inference), Unbound slots), so step 2 of [§10.4](#104-best-candidate) makes the pair incomparable. A default never breaks an ambiguity. After the defaults, the selected candidate's checks that needed the slot (fits, Constraints) use the defaulted Type; a failure is an error and never reselects. A slot still unbound after the defaults is an error.

An expression without an expected Type uses the defaults directly. `null`, empty collections and untyped functions have no fallback Type.

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(1)        // Error: ambiguous; both integer Types fit.
choose(1@i64)    // choose(i64).

func f(value: i32) -> () => ()
func f<T>(value: T) -> () => ()
f(1)             // Error: ambiguous; T is unbound while candidates are compared.
f(1@i32)         // f(i32): T = i32 is evidence, and step 3 prefers the non-generic function.

func g<T>(value: T) -> () => ()
g(1)             // The only candidate; T defaults to i32 after selection.
```

### 10.2.2. Inference and adaptation classes

Inferring a Type performs no Copy, Move or borrow.

1. Explicit Type arguments bind first; the rest are inferred together from the original complete Types of the receiver and the arguments, never by traversal order. A bare `T` binds the argument's Type, except that a parameter of a read Type ([§3.5.3](03-types-and-values.md#353-value-read)) binds the terminal referent of reference layers, and the argument is value-read. A declared `ref/T` or `uniq/T` binds `T` to an owned argument's Type or to the referent of a borrow's outermost layer. A pair layer ([§13.5.5](13-operators-and-assignment.md#1355-follow)) is not a borrow: for an element Type `s/U`, `inspect(c[i])` binds `T = s/U` and borrows the slot, while `inspect(c[i]@follow)` binds `U`.
2. An independently known expected result fills still-unbound parts by Type matching; it never inverts a borrow or value read and never changes an established Type.
3. Each argument's acquisition and adaptation are planned under [§3.5](03-types-and-values.md#35-copy-and-move) and [§10.2](#102-common-adaptation). A directly fitting exclusive reference is Reborrowed when bare and transferred under `@move`.

```kimi
// n: ref/i32; r: uniq/Node; identity<T>(value: T) -> T; double<T> with T is PrimitiveInteger.
let a = identity(n)        // T = ref/i32.
let b = identity<i32>(n)   // Value read at the fixed i32.
let c = identity(r)        // T = uniq/Node; an exclusive Reborrow, like let saved = r.
let e = double(n)          // T = i32; n is value-read.
```

Adaptations are ranked by class, best first, with no preference within a class:

| Class | Meaning |
| --- | --- |
| Exact | No adaptation operation, including no borrow or Reborrow |
| Literal fitting | Fitting a Literal argument ([§10.5.1](#1051-argument-kinds)) directly to the candidate's Type; when an acquisition such as borrowing a temporary is also needed, that acquisition's class applies |
| Same-Semantics Reborrow | A Reborrow that preserves the input's outer Semantics |
| Cross-Semantics adaptation | Any other borrow or Reborrow, one shared reference through several layers, or a value read |

Exact concerns Types: a by-value argument still Copies when bare or transfers under `@move`, with no preference between them, and a required exclusive Reborrow is never Exact. Permissions and the Place-versus-temporary form are checked during applicability; initialization and Loan conflicts after selection ([§10.6](#106-usage-legality-and-operators)), with call borrow reservations ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)). A generic body checks acquisition and ranking from its declared Constraints at definition; instantiation never reselects. Erasure belongs to no class: two erasures at one argument are equal, and an erasure and any other row are incomparable.

## 10.3. Expected results

An expected result may complete inference (step 4 of [§10.1](#101-candidate-applicability)) and excludes a candidate only when the use position admits no acquisition or adaptation of its known result. It never ranks candidates. Only the structural part of the fit is judged before selection; Origin relations are judged after ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)), while Constraints involving Origins (`Owned`, `Callable`, Type identity) still take part. No conversion is inserted to keep a candidate. Discarding a call supplies no Unit expectation ([§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts)).

| Use position | Rule applied to the known result |
| --- | --- |
| Fixed expected Type | Common adaptation ([§10.2](#102-common-adaptation)) with the position's lifetime check |
| No expected Type | Bare acquisition ([§3.5](03-types-and-values.md#35-copy-and-move)) |
| Explicit operation or discard | That operation's rule |

An unknown result `T` is never inferred backward from the adapted Type.

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // fetch(i32): only its result fits.
fetch(1)                    // Error: discarding leaves both candidates.

func read(x: i32, from: ref/i32) -> ref/i32 => from
func read(x: i64, from: ref/i32) -> i32 => 0
// let other: i32 = read(1, limit@ref) // Error: both results fit, by a value read or exactly.
```

## 10.4. Best candidate

Pairwise comparison yields better, worse, equivalent or incomparable; only equivalent pairs proceed to the next step. A candidate is selected only if it is better than every other applicable candidate:

1. For each explicit source argument, compare the adaptation classes ([§10.2.2](#1022-inference-and-adaptation-classes)); the receiver is excluded ([§7.3](07-functions-and-callable-values.md#73-receivers)). A dominates B when it is no worse everywhere and better somewhere; opposing advantages are incomparable. Named arguments are matched by source expression. Defaults are excluded, and costs are never summed.
2. For each source argument, compare the substituted parameter Types that step 1 matched to it by their structural part (Origins ignored); parameters filled by defaults are not compared. If all are identical, go to the next step; otherwise the candidates are incomparable.
3. Prefer a function with no generic parameters of its own, including length parameters; a generic enclosing Type does not count.
4. Prefer fewer defaults used.
5. Otherwise the call is ambiguous (error).

Widths, Constraint strength, pattern generality and declaration order never rank candidates, and a later step never rescues an earlier incomparability. The two `inspect` keys below do not overlap ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)).

```kimi
func inspect(value: uniq/(uniq/Node)) -> () => ()
func inspect(value: ref/Node) -> () => ()
func work(x: uniq/(uniq/Node))
    inspect(x)          // The first: a same-Semantics Reborrow beats a cross-Semantics reference.
    inspect(x@follow)   // The second: only it adapts the selected uniq/Node Place.

func g(x: i32) -> () => ()
func g(x: i32, y: i32 = 0) -> () => ()
g(1@i32)                // g(x): steps 1–3 tie on x; step 4 prefers fewer defaults.
```

## 10.5. Inference boundaries

- Local Types are fixed at their declaration; fixed-array annotation holes ([§4.3.1](04-arrays-indexing-and-slices.md#431-array-literals-and-annotation-holes)) are resolved from that initializer.
- Named functions have a written result or Unit ([§7.1](07-functions-and-callable-values.md#71-declarations-bodies-and-results)); bodies and callers never infer their signatures. Anonymous functions are inferred ([§7.7.1](07-functions-and-callable-values.md#771-syntax-and-inference)).
- Explicit specializations take no part in inference or applicability; one is chosen after the original declaration and its arguments are determined.
- Substitution preserves bound Origins and Loan requirements, including nested ones, and creates no borrow ([§15](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis)).
- A `try` operand has no expected Type ([§17.3](17-failure-handling.md#173-try-propagation)), nor does an explicitly discarded expression ([§14.2.4](14-control-flow.md#1424-explicit-discard)).

### 10.5.1. Argument kinds

Each argument of a call is exactly one of the following, except a Tuple or array literal argument, which is not classified itself: each of its elements is, one by one and recursively, as an argument at its element position.

- **Literal**: an untyped literal, a literal-only expression ([§12.3](12-expressions.md#123-literal-typing)), or a control-flow expression whose result sources ([§14.9](14-control-flow.md#149-result-validation)) other than those of Type Never ([§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type)) are all such, at least one of them. Each candidate's parameter Type propagates to every source; it is ranked by the Literal fitting row of [§10.2.2](#1022-inference-and-adaptation-classes); it is no evidence for a slot, and its default applies after selection ([§10.2.1](#1021-literals)).
- **Independently typable**: any other argument whose complete Type is fixed with no expectation from the call and no literal default, such as a typed value or Place (with any explicit `@` operation) or a function reference that does not need `S` ([§10.5.3](#1053-function-references)). A nested call qualifies only when it has exactly one applicable candidate without an expected result. An anonymous function never qualifies. Its Type is evidence for the outer candidate's slots.
- **Waiting**: every other argument, such as an anonymous function, a function reference that needs `S`, or a nested call that is not independently typable. It is checked only against the expected Type shared by the remaining outer candidates, or that of the selected candidate, and is never evidence for an outer slot.

A Tuple or array literal argument is fitted per candidate ([§4.3.1](04-arrays-indexing-and-slices.md#431-array-literals-and-annotation-holes)): a Literal element fits the Type at its position and is no evidence, an independently typable element is evidence for the slots at its position, and a waiting element is checked there as a waiting argument is. A slot that takes the whole literal is bound from it only when every element is evidence. A control-flow argument whose Tuple or array result, a fill's included, is formed part by part ([§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type) step 2) is classified and fitted as such a literal argument is, each position taking the parts its sources hold there, other than Never, as the sources of one control-flow argument.

Inner calls are never resolved once per outer candidate. Eliminated candidates are not revived. A waiting nested call that neither a shared expectation nor an already selected candidate types requires an annotation, explicit Type arguments or a typed binding; anonymous functions follow [§10.5.4](#1054-anonymous-functions).

```kimi
func identity<T>(value: T) -> T => value@move
func take(value: i64) -> () => ()
func makeValue() -> i64 => 1
func inspect<T>(value: ref/T) -> () => ()
func makeBox<T>(value: T) -> Box<T> => Box.init(value@move)  // Box<T>: an application struct.
func outer<U>(value: Box<U>) -> () => ()
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
func pair<T>(first: T, rest: (T, T)) -> () => ()

inspect(makeValue())            // Independently typable: T = i64.
take(identity(1))               // Waiting: the expected i64 fixes the inner T.
outer(makeBox(1))               // Error: U is unbound; a waiting argument is no evidence. Write makeBox(1@i32).
pair(1, (makeValue(), identity(2))) // Elements one by one: T = i64 from makeValue(); identity(2) waits.
take(if ready => 1 else => 2)   // Literal: both sources fit i64.
choose(if ready => 1 else => 2) // Error: ambiguous; both candidates fit the literals.
```

```kimi
func identity<T>(value: T) -> T => value@move
func fail() -> Never => $abort("no value")
func pick(ready: bool, large: i64) -> i64
    let both = identity(if ready => (large, (return 1)) else => (large, 5))         // T = (i64, i32) after selection.
    let one = identity(if ready => 1 else => fail())                                // Literal: T = i32 after selection.
    return both.0
```

### 10.5.2. Call signatures

The **known call signature** of a Type is the signature a value of it is called with ([§7.7.3](07-functions-and-callable-values.md#773-call-receiver)): a Function Item's declaration signature with bound arguments substituted, a concrete Closure's internal signature, a common Function Type's structure, or, for a generic parameter, the one signature its `Callable` Constraints give ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)). An anonymous function supplies only the parameter and result Types written in its header, and has a task slot exactly when it writes one ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)).

A position has a **fixed expected call signature** `S` when its expected Type is a common Function Type (then `S` is that Type), or is `F`, `ref/F` or `uniq/F` for an unbound slot `F` whose `Callable` Constraints all give one signature `S`. With several outer candidates, only an `S` common to all is used. The slots in `S` are unknowns for [§10.8](#108-generic-argument-inference); only parts without unbound slots guide the argument.

```kimi
func make<T>(action: () -> T) -> T => action()
func makeLarge() -> i64 => 2
let large = make(makeLarge)               // T = i64 from makeLarge's signature; then erased.
let written = make(func () -> i64 => 2)   // T = i64 from the written result Type.
// let open = make(func () => 2)          // Error: only the body could fix T.
```

### 10.5.3. Function references

A reference's candidates are its group's declarations that pass step 1 of [§10.1](#101-candidate-applicability); a Type-qualified instance reference takes `self` as an ordinary parameter ([§7.3](07-functions-and-callable-values.md#73-receivers)). It **needs `S`** when two or more candidates remain or a candidate has own slots that explicit arguments do not bind. Without `S`, such a reference is an error; one that does not need `S` is independently typable ([§10.5.1](#1051-argument-kinds)).

A reference that needs `S` is selected once every parameter Type of `S` is closed. Each candidate binds its own slots by matching its parameter Types against those of `S`, and its result against the closed parts of the result of `S` ([§10.8](#108-generic-argument-inference)), without adaptation. It is applicable when all slots are bound, its Constraints and premises pass step 5 of [§10.1](#101-candidate-applicability) (an Unknown judgment follows the Unknown applicability rule there), and its substituted signature is compatible with the closed parts of `S` ([§10.7](#107-callable-signature-compatibility)), including arity and task-slot presence. Candidates are compared by [§10.4](#104-best-candidate), where steps 1 and 4 tie.

After selection, the unsafe and `drop` prohibitions and the erasure conditions ([§7.6](07-functions-and-callable-values.md#76-unsafe-functions), [§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)) are checked and never reselect. At `F`, `ref/F` or `uniq/F`, `F` binds to the Function Item Type without erasure; at `ref/F` the temporary is borrowed. The selected signature never fills an open result of `S`.

A requirement referenced through a constrained Type, such as `T.compare` under `T is Comparable`, is the requirement's Function Item with `Self` and associated Types substituted; its calls reach the implementation ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)). It stays the requirement's Function Item after instantiation. `Comparable.compare` is invalid.

```kimi
func show(value: i32) -> () => ()
func show<T>(value: T) -> () => ()
func identity<T>(value: T) -> T => value@move

let a: (i32) -> () = show         // show(i32): step 3 prefers the non-generic function.
let b: (i32) -> i32 = identity    // T = i32 from S; then erased.
// let e = show                   // Error: no S and two candidates.
// let f = identity               // Error: no S and T is unbound.
// let g: (ref/Node) -> ref/Node = identity // Error: T would hold a per-call Origin.
```

### 10.5.4. Anonymous functions

Explicit Types, independently typable arguments ([§10.5.1](#1051-argument-kinds)) and Constraints are processed before an anonymous body, in any argument order. Header arity and Types may filter candidates and are evidence. The body is checked against its written signature, a common remaining expectation, or the selected candidate's signature. At `F`, `ref/F` or `uniq/F`, the fixed expected call signature ([§10.5.2](#1052-call-signatures)) guides omitted parameter Types and `F` binds the concrete Closure Type without erasure. When candidates share no expectation, they are compared without the body. A part the body needs but no expectation supplies requires an annotation. Bodies never select candidates and are never retried, and parameters are never inferred from later uses.

If the return Type is Unit before the body is checked, a single-item body is discarded ([§7.1](07-functions-and-callable-values.md#71-declarations-bodies-and-results)); otherwise the body is in Value Context; a return Type inferred from it belongs to the anonymous function only and is never outer evidence ([§10.5.1](#1051-argument-kinds)). Unit fixed by another argument before the body is checked counts; a body is never reinterpreted after later inference, and a typed function value never erases its return to Unit.

```kimi
func run(action: () -> ()) => action()
func run(action: () -> i32) => action()
run(func () => compute())        // Error: the candidates differ in expectation; the body never selects.
run(func () -> i32 => compute()) // The written result Type selects the second.
```

### 10.5.5. Constraints after inference

Constraints are judged by [§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof): Proven satisfies, Refuted rejects, Error reports invalid evidence, and Unknown follows step 5 of [§10.1](#101-candidate-applicability). Generic capabilities are proven when the definition is checked ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)); failing to prove `T is C` never proves `T is not C`. Members keep their definition environment, never the caller's imports ([§18](18-modules-and-dependencies.md#18-modules-and-dependencies)). There is no theorem proving, Type enumeration or Constraint-strength ranking. Declarations in excluded syntax never enter candidate sets ([§19.4](19-compile-time-directives.md#194-excluded-syntax)); conditional members stay in lookup and affect applicability only ([§7.5](07-functions-and-callable-values.md#75-function-constraints), [§8.8](08-generics-constraints-and-contracts.md#88-conditional-conformance)).

## 10.6. Usage legality and operators

After selection, unsafe permission, initialization, Move state, Loans, lifetimes, accessor access and write capability are checked. A failure is an error and never changes the selection, so adding a better overload can invalidate existing calls.

An implementation may bound resolution work; exceeding the bound is an error, never a selection.

Built-in operations, comparison mappings ([§13.4.1](13-operators-and-assignment.md#1341-contract-comparison-mapping)) and the arithmetic Contracts ([§13.3.4](13-operators-and-assignment.md#1334-route-selection)), which own their selection, define operator candidates. The Indexable Contracts define index-expression candidates; the key selects one conformance by unique fit, without ranking or expected-result filtering ([§4.7.1](04-arrays-indexing-and-slices.md#471-index-expression-resolution)).

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: i64) -> () => ()
let number: i32 = 1
inspect(number) // Error: inspect(i32) is selected, then rejected outside an Unsafe Block.
```

## 10.7. Callable signature compatibility

Common Function Type erasure ([§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)) and `Callable<r, S>` ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)) use this rule. An implementation `(A1, ..., An) -> R` is compatible with a required `(P1, ..., Pn) -> Q` when both or neither have a task slot ([§24.2.4](24-suspension-and-asynchronous-tasks.md#2424-compatibility)), the arities are equal, `Pi <: Ai` ([§3.8](03-types-and-values.md#38-type-relations-and-operations)) at every position, and `R` is result-compatible with `Q`. Labels and defaults cannot bridge a mismatch.

**Result compatibility.** An implementation result `R` is compatible with a required result `Q` when `R <: Q` or `R` is Never. Contract implementation matching, refinement conflict checks, Callable compatibility and common Function Type erasure all use this one relation. A value of one common Function Type relates to another only by subtyping, and Never fitting ([§3.8](03-types-and-values.md#38-type-relations-and-operations)) is a separate relation from subtyping.

Origins and Loans are compared by canonical Origin contracts ([§15.3.7](15-ownership-and-lifetime-analysis.md#1537-contract-comparison)): required quantifiers are rigid, and only the implementation's call-time Origins are inferred. Compatibility inserts no borrow, conversion or erasure. A direct match and an erasure are incomparable ([§10.2.2](#1022-inference-and-adaptation-classes)).

```kimi
contract Empty
    func empty() -> Self

struct Impossible
    Self is Empty
    public func empty() -> Never => $abort("no value")   // OK: Never satisfies Self.
```

## 10.8. Generic argument inference

Explicit arguments bind first. Type, Semantics, length ([§4.4](04-arrays-indexing-and-slices.md#44-function-length-parameters)) and Origin constraints are then collected together from the receiver and the independently typable arguments ([§10.5.1](#1051-argument-kinds)), and the expected result fills only unbound parts. A call without explicit Type arguments to a group whose validity relies on correlated slots must establish each correlation as [§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape) requires.

**Call-signature matching.** Each parameter with a fixed expected call signature `S` ([§10.5.2](#1052-call-signatures)) matches `S` against the known call signature of its argument, or for `F`, `ref/F` or `uniq/F` of the Type the argument binds to `F`. Task-slot presence, structure and Semantics match by normalized identity and Origins by limited inference ([§15.3.6](15-ownership-and-lifetime-analysis.md#1536-origin-inference)), without adaptation or search. Only parts containing an unbound slot are matched this way; other parts must fit as [§10.7](#107-callable-signature-compatibility) requires (erasure at a common Function Type, the Callable Constraint at `F`), so header Types need only be compatible. A per-call Origin never solves an Origin inside a slot: `(T) -> ()` against `(ref/i32) -> ()` gives `T = ref/i32` with its Origin left to other evidence.

**Solving.** A failed match, or conflicting structure or Semantics evidence, makes the candidate inapplicable; Origin evidence never does ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)). An explicit Type argument is kept. Otherwise the slot's solution is its binding at invariant positions: all such bindings must agree in structure and Semantics (a disagreement makes the candidate inapplicable), and their Origins must be equal. Without an invariant binding, the solution is the meet of the covariant bindings. Each argument's Origin fit to the solution is judged after selection. An unresolved associated projection such as `T.Element` is never inverted: an argument of Type `i32` at `value: T.Element` binds no `T`, and another parameter such as `witness: Tag<T>` must bind it. Traversal order, conversion chains, common-base search and Constraint strength never choose a solution. A waiting argument's completed Closure fills no outer slot. An argument or element of Type Never supplies no evidence, even for a slot that takes a whole Tuple or array literal holding it, as Never supplies no candidate in [§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type); it still fits its position ([§3.8](03-types-and-values.md#38-type-relations-and-operations)).

**Semantics.** Under an expected `s/T`, the Literal operand of `n@s` fits `T` (not for object targets, [§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)); a typed operand keeps its Type. An unbound `s` is inferred from the source's outer Semantics with no conversion search: `owner` and `ref` evidence for one `s` conflict. `s is reference` checks the Semantics projection and the pair's `T is C` its target projection; an ordinary `T` keeps its complete Semantics.

**Unbound slots.** A slot that no evidence binds once steps 3 and 4 of [§10.1](#101-candidate-applicability) are complete (literal defaults are not yet applied, [§10.2.1](#1021-literals)), such as one only a Literal or waiting argument could fix, is not a mismatch: the candidate stays applicable on its other checks, and Constraints needing the slot are not judged. A parameter Type containing it is identical to, and a subtype of, no other Type. A parameter `F`, `ref/F` or `uniq/F` bound by a waiting argument is not such a slot: equal forms compare equal in step 2 of [§10.4](#104-best-candidate). Infinite substitutions such as `X = ref/X` are errors, and cycles supply no evidence.

```kimi
func both<T, F, G>(first: ref/F, second: ref/G) -> T
    F is Callable<() -> T>
    G is Callable<() -> T>
    return first()

func makeSmall() -> i32 => 1
func makeLarge() -> i64 => 2
func identity<T>(value: T) -> T => value@move
func stop() -> Never => $abort("stop")
// let mixed = both(makeSmall, makeLarge)  // Error: conflicting evidence for T.
// let s = identity(stop())                // Error: T is unbound; Never supplies no evidence.
let t: i32 = identity(stop())              // T = i32 from the expected result.
```

### 10.8.1. Constructor Type inference

Construction applies [§10.8](#108-generic-argument-inference) to the selected structure's own slots after its declaration and environment are fixed ([§9.7](09-names-signatures-and-access.md#97-type-name-selection)). An expected result supplies slots only by direct structural match with an owner Type of the same declaration, never by inverting a borrow, object creation, upcast, conversion or associated projection. Defaults, Field initializers and constructor bodies supply no evidence. Literal arguments fit and default as in [§10.2.1](#1021-literals). Enum Cases follow [§6.5.2](06-declarations-and-containers.md#652-case-references).

**Fixed-binding check.** An inferred construction is accepted only if fixing its inferred construction Type explicitly would select the same constructor from the original group, with the original argument mapping and untyped literals. Otherwise explicit Type arguments are required; candidates are never switched. A waiting argument needing another context also requires them; waiting alone never rejects a single candidate.

```kimi
func wrap<T>(value: T) -> Box<T>
    return Box.init(value@move)   // T from the argument.
// For C<T>.init(x: ref/i32, y: ref/T) and init(x: ref/T, y: ref/T),
// C.init(x, y) with x: ref/(ref/i32) and y: ref/i32 is an error:
// inference picks the first, but with C<i32> fixed both are ambiguous.
```

## 10.9. Excluded forms

Implementations must not add constructor-own generic parameters, structure length parameters, implicit adaptations beyond [§10.2](#102-common-adaptation), operator candidates beyond those of [§10.6](#106-usage-legality-and-operators), or explicit overload-selection syntax; other generic forms outside this revision are listed in [§D.2](appendices/D-deferred-features.md#d2-declarations-functions-and-generics).
