# 10. Overload resolution and inference

[Specification index](../SPEC.md)

Container qualifiers are resolved and bound under §9.6.1 before call inference. This chapter infers only a function's own arguments; an omitted outer Container environment is never inferred from call arguments or an expected result. Candidate identity includes the retained environment, and a failed later constraint does not reopen qualifier lookup.

The distinct relations of [Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations) apply: argument adaptation, expected-result compatibility and acquisition legality are separate judgments. A successful subtype proof neither selects nor authorizes a value operation.

## 10.1. Candidate applicability

Each declaration in the committed function group is checked against the following requirements, subject to the shared-expectation and body-checking boundaries of §10.5. These steps do not authorize checking a nested call or anonymous body separately for each candidate.

1. Validate the explicit Type-argument count and kinds.
2. Match positional and named arguments and record omitted defaults.
3. Infer Type arguments from the receiver and the explicit arguments.
4. Use an independently known expected result Type to fill the remaining Type arguments, without changing those already fixed.
5. After substitution and before Best Candidate comparison, check permitted argument adaptations, Function Types, Constraints and any conditional-member premises (§8.4.8).
6. Reject instantiated result Types incompatible with the expected result, if one exists.

Zero applicable candidates is an error. Candidate checking records plans; it neither executes nor commits runtime Copy/Move, Loans or defaults. Errors in declarations, such as unknown Types, malformed Constraints or duplicate Signatures, remain declaration errors even when another candidate succeeds.

**Argument matching.** Positional arguments precede named arguments and bind parameters in declaration order. At a direct call, an ordinary parameter accepts a positional argument only when its external name carries `?` (§7.2). Positional matching never skips a name-required parameter, a parameter with a default, or a parameter supplied later by name, and never searches by Type for another position. A bound method first removes its receiver position from this matching sequence (§7.3). Named arguments use external names, may be reordered, and cannot bind a parameter twice. An unbound ordinary parameter is filled by its default if present; otherwise a missing-argument error is required, irrespective of `?`. Unknown labels, excess positional arguments and positional arguments targeting name-required parameters are rejected. Explicit arguments are evaluated in source order, then omitted defaults in parameter order; defaults follow the [declaration-site rules](07-functions-and-callable-values.md#72-parameters-and-defaults) and supply no generic-inference evidence. Function-value calls supply every argument positionally.

```kimi
func scale(value?: i32, by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // Valid; evaluate by before value.
scale(3, value: 4)     // Error: value supplied twice.
scale(3, factor: 4)    // Error: factor is an internal name.

func configure(mode: i32 = 0, count?: i32 = 1) => ()
configure(count: 3) // Valid; use the default for mode.
configure(3)        // Error: mode requires its name; do not skip to count.

func pair(first?: i32 = 1, second?: i32 = 2) => ()
pair(3)            // first = 3, second = 2.
pair(second: 3)    // first = 1, second = 3.
```

## 10.2. Argument adaptation and literals

Adaptations are compared in this order, best first:

| Class | Meaning |
| --- | --- |
| Exact | Normalized Type compatibility requiring no adaptation operation, including no borrow or reborrow |
| Literal fitting | Directly fitting an unresolved literal to the candidate's Type |
| Same-Semantics reborrow | A reborrow that preserves the input Type Semantics |
| Cross-Semantics borrow/reborrow | Any other permitted borrow or reborrow |

Exact describes Type adaptation, not value transfer: an Exact by-value argument still Copies or Moves under [Copy and Move](03-types-and-values.md#35-copy-and-move), and Copy versus Move adds no ranking preference. Origin subtyping that needs no value operation remains permitted.

The initial borrow adaptations are listed below. In the owner-Place and owner-temporary rows `T` has `owner` Semantics; in the value-reborrow rows it is the complete immediate Referent Type. Adding a layer around an existing reference or object handle requires a fully specified explicit [storage-borrow target](13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow); it is not an implicit argument adaptation.

| Input | Expected | Operation / class |
| --- | --- | --- |
| Readable `T` Place | `ref/T` | Shared borrow / cross |
| Owner `T` temporary | `ref/T` | Materialize once and shared-borrow / cross |
| Exclusively writable `T` Place | `uniq/T` | Exclusive borrow / cross |
| `uniq/T` | `uniq/T` | Call reborrow / same |
| `uniq/T` | `ref/T` | Shared reborrow / cross |
| Accessible `obj/T` Place | `objref/T` | Shared object borrow / cross |
| Exclusively writable `obj/T` Place | `objuniq/T` | Exclusive object borrow / cross |
| `objuniq/T` | `objuniq/T` | Object call reborrow / same |
| `objuniq/T` | `objref/T` | Shared object reborrow / cross |

A required exclusive reborrow is not Exact even when the written Types match. Type and declaration permissions and the Place-versus-temporary form are checked during applicability; flow-dependent initialization and active Loan conflicts are checked after selection. Borrow adaptations neither extend lifetimes nor duplicate ownership. Selected exclusive adaptations use [call reservation and activation](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations); these checks do not change applicability or ranking. No further `rc`/`arc`, exclusive-temporary or outer-layer borrow adaptations are inferred from this table.

[Inherited receiver projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection) defines its member-only operations and rankings separately; they do not apply to ordinary arguments or unbound calls. Same-complete-Type Sealed payload receiver projection (§12.4.4) ranks as a cross-Semantics Borrow/Reborrow: ordinary lookup, applicability, comparison and access checking run first, and the selected receiver is evaluated and applied once. It adds no implicit projection to ordinary arguments and grants no inherited-base completeness.

Fixed-expectation [common function conversion](07-functions-and-callable-values.md#764-function-references-and-common-type-conversion) is handled separately and adds no rank to this table. There are no implicit object upcasts, numeric-width, signedness, integer/float or user-defined conversions, and no unlimited dereference or conversion chains; raw dereference is explicit. The existing Never and Origin rules remain Type rules and add no overload priorities.

**Literals.** An untyped integer literal fits any representable candidate integer Type directly; floating literals follow the numeric rules. Literals are not defaulted to `i32`/`f64` before fitting, narrower widths are not preferred, and defaults never break overload ambiguity. Outside candidate comparison, an independent expression without an expected Type uses the ordinary numeric defaults. Generic inference processes receiver, other-argument and known-result constraints before defaulting. `null`, empty collections and untyped functions gain no universal fallback Type.

```kimi
func choose(value?: i32) -> () => ()
func choose(value?: i64) -> () => ()
choose(1)      // Error: both integer Types fit.
let x = 1      // Independently defaults to i32.
choose(x)      // Exact i32.
choose(1@i64)  // Exact i64.
```

**Borrowed temporaries.** An owner temporary can be shared-borrowed for **any** `ref/T` argument, not only for collection keys. It is evaluated once and materialized in a Temporary Place. A known source Type can infer `T` as for an owner Place; the target need not already be fixed. For an unfitted literal, receiver, other-argument and known-result constraints are processed first, then candidate fitting and ordinary value defaults as above. The result ranks as a cross-Semantics borrow, below Exact and direct literal fitting, and defaults do not break ties.

Copying or reborrowing an existing reference takes priority over adding a layer: this rule adds no implicit `uniq` temporary borrow, outer borrow of an object handle, or outer layer around an existing reference. For example, a key `K = ref/X` needs an explicit `@ref/ref/X` to borrow its slot.

The materialized owner lasts until the normal outermost expression, condition or match temporary boundary (§3.6), and at least through the call. It is neither shortened to the end of the call nor extended to keep a returned borrow valid. On an escape failure, the diagnostic identifies the borrow, the temporary's end and the required use, and suggests a named local only when its Type and Origin constraints can work.

```kimi
func inspect<T>(value?: ref/T) -> () => ()
func makeValue() -> i64 => 1
inspect(makeValue()) // Infer T = i64 and borrow the temporary.
inspect(1) // T defaults to i32 after constraints.

func chooseBorrow(value?: ref/i32) -> () => ()
func chooseBorrow(value?: ref/i64) -> () => ()
chooseBorrow(1) // Error: both fit; default i32 does not break the tie.
```

## 10.3. Expected results

This section is the static expected-result judgment of [the relation table](03-types-and-values.md#38-type-relations-and-expression-operations), not the implicit-expression-adaptation judgment.

An expected result may complete inference and exclude otherwise applicable candidates. Compatibility requires normalized Type identity or a defined subtype relation without additional value operations. Origins are instantiated and checked, including permitted covariant shortening. No new borrow or reborrow, dereference, numeric conversion or user conversion is inserted to keep a candidate, and a function body's literal is never retyped to change its established return Type.

Expected results do not rank candidates by result-conversion quality. Result Loan/Origin propagation and Copy/Move still apply. An expectation comes from a surrounding annotation, a fixed parameter or a declared result, subject to §10.5; it cannot circularly select its own source candidate. Discarding a call supplies no expected Unit Type. Constructs with Unit-fixed Target Result Types follow §14.2, and their directly discarded calls still receive no Unit expectation.

```kimi
func fetch(value?: i32) -> string => "text"
func fetch(value?: i64) -> i64 => value
let text: string = fetch(1) // Selects fetch(i32) by result compatibility.
fetch(1)                   // Error: discarding leaves both candidates.
```

These declarations have distinct parameter Signatures; declarations that differ only in return Type are invalid regardless of call-site expectations. A candidate returning `T` cannot survive an expected `ref/T` by borrowing its result, and a `uniq/T` result cannot become `ref/T` through a newly inserted reborrow.

## 10.4. Best candidate

Pairwise comparison yields better, worse, equivalent or incomparable. **Only equivalent candidates proceed to the next step.** A candidate is selected only if it is better than every other applicable candidate:

1. Compare adaptation quality for the receiver and each explicit source argument. A dominates B only if it is no worse everywhere and better somewhere; all-equal proceeds, and opposing advantages are incomparable. Named arguments are matched by the same source expression, not by candidate parameter order. Defaults are excluded, and numeric costs are never summed.
2. Compare substituted parameter Types at the same positions. A dominates B if every Type is equal or a defined subtype and at least one is a strict subtype; all-equal proceeds, and unrelated Types or opposing subtype advantages are incomparable.
3. Prefer a function with no generic parameters of its own, including length parameters. A generic enclosing Type alone does not make the function generic.
4. Prefer fewer defaults used by this call.
5. Otherwise, report ambiguity.

Numeric Types are not ranked by width, Constraints not by strength or clause count, and generic declarations not by general pattern partial ordering. `uniq/T <: ref/T` is never invented from the ability to reborrow. Incomparability at an earlier step cannot be rescued by nongeneric status or fewer defaults, and declaration, file, alias and name order never break ties.

```kimi
func inspect(value?: ref/i32) -> () => ()
func inspect(value?: uniq/i32) -> () => ()
var x: i32 = 0
inspect(x)      // Error: both cross-semantics borrows; Types incomparable.
inspect(x@ref)  // Shared candidate: Exact.
inspect(x@uniq) // Exclusive candidate: same-semantics beats cross-semantics.
let action: (uniq/i32) -> () = inspect
```

The exclusive candidate wins for an exclusive input because its Semantics match, not because exclusivity is stronger. Candidates `(i32, ref/i32)` and `(ref/i32, i32)` are incomparable for two `i32` locals. Likewise, `f<T>(T)` and `f<U>(Box<U>)` remain tied for `Box<i32>` when substitution makes both parameter Types equal and the later steps tie.

These rules deliberately leave owner-to-`ref`/`uniq` overloads ambiguous; any preference would require a language revision. Field Move eligibility and its generic limits follow [Field Move](11-properties.md#1112-move-paths-and-inherited-fields).

## 10.5. Inference boundaries and specialization

Local Types are fixed at their declaration, and later uses cannot infer them backward. [Named functions](07-functions-and-callable-values.md#7-functions-and-callable-values), including local functions, methods and Contract requirements, have an explicit result or Unit, independent of access and body form; neither bodies nor callers infer their signatures. [API accessibility](09-names-signatures-and-access.md#932-api-signature-accessibility) is checked, and substituting a written generic result does not reanalyze the body. Local binding inference and [anonymous-function inference](07-functions-and-callable-values.md#761-syntax-and-inference) remain available.

Explicit Type arguments follow the [slot rules](08-generics-constraints-and-contracts.md#81-generic-type-parameters), and omitted defaults do not infer generic arguments. Explicit specializations take no part in inference or overload applicability; an implementation is selected only after the original declaration and its static generic arguments are determined.

Generic inference and substitution use the [complete-Type slots and projections](08-generics-constraints-and-contracts.md#81-generic-type-parameters) and preserve all bound Origins and inferred Loan requirements, including nested dependencies. A surrounding borrow such as `ref/T` keeps `T`'s internal dependencies alongside its own Origin and Loan. Substitution alone creates no Borrow or Reborrow, releases no Loan and extends no lifetime; actual call-site checks follow the [ownership and Origin rules](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis).

**Nested calls.** Bidirectional checking supports literals, function references, anonymous functions and expressions directly checkable against a candidate Type. It does not search combinations by rerunning an inner overload resolution for every outer candidate in `f(g(x))`:

- Independently typable arguments and explicit Type information are processed first.
- An inner expression may use an expected Type shared by all remaining outer candidates.
- If one outer candidate is already determined, its expectation is used, and eliminated candidates are not revived after a failure.
- Otherwise an annotation, explicit Type arguments or a typed intermediate binding is required.

```kimi
func inner(value?: i32) -> i32 => value
func inner(value?: i64) -> i64 => value
func outer(value?: i32) -> () => ()
func outer(value?: i64) -> () => ()
outer(inner(1))                    // Error: would require nested search.
let intermediate: i64 = inner(1)  // Expected result fixes the inner call.
outer(intermediate)
```

**Function references** are resolved with ordinary evidence, including explicit generics or a fixed expected callable signature. A unique declaration needs no expected Type, and an unresolved overload set is not a value. Function values have no labels or defaults and cannot name unsafe functions or `deinit`. A conformance failure cannot change the chosen overload or capture mode.

**Anonymous body context.** Explicit Types, independently typable arguments and generic constraints are processed before the body is checked, independently of argument order. Arity and explicit Types may filter candidates. The written signature, a common remaining expectation, or an already selected candidate's signature is used; `Callable<r, S>` may guide the parameters while `F` keeps its concrete Closure Type. If unresolved candidates cannot provide the needed expectation, an annotation is required. Return expressions are never inspected to select candidates, bodies and captures are never retried across candidates, and parameters are never inferred from later uses.

If the normalized return Type is Unit before the body is checked, the single-item expression is discarded (§7.1); otherwise it is in Value Context, including for return inference. Unit inferred from another argument before body checking is allowed, whatever its spelling or source. The body and captures are not reinterpreted after later Unit inference or generic instantiation. A standalone lambda without an expectation can infer its return, while an already typed function value cannot erase its return to Unit. There is no overload preference between using and discarding lambda results.

~~~kimi
func run(action?: () -> ()) => action()
func run(action?: () -> i32) => action()
run(func () => compute()) // Error: differing expectations; compute returns i32.
run(func ()
    return compute()
) // Same error: an explicit return does not select an expected signature.
run(func () -> i32 => compute()) // Explicit return Type selects the second overload.
func apply<U>(value?: U, action?: () -> U) -> U => action()
apply((), func () => compute()) // U is Unit before checking the lambda; discard its value.
// func make<T>() -> T => 123 is invalid for arbitrary T; instantiation cannot rescue it.
~~~

**Constraints after inference.** The [limited proof system](08-generics-constraints-and-contracts.md#87-constraint-proof-system) applies: Proven satisfies a requirement, Refuted rejects it, and Error diagnoses invalid or contradictory evidence. Unknown may keep only a legitimate dependency that resolves by its deadline; it proves neither applicability nor negation. Generic capabilities must be proven at definition acceptance (§8.10). Nondependent Names bind at the definition and use the declared Constraints; failing to prove `T is C` does not prove `T is not C`. Deferred members keep the [definition environment](18-modules-and-dependencies.md#18-modules-and-dependencies), never caller imports. All necessary Constraints are proven before concrete finalization. Arbitrary theorem proving, Type enumeration and constraint-strength ranking are not allowed.

Environment-selected membership follows §19.4: excluded declarations neither merge nor enter candidate sets. Conditional-conformance members (§8.4.8) remain in ordinary lookup; their published conditions affect applicability, not lookup stopping or syntax selection. Each defining generic environment is preserved.

## 10.6. Usage legality and operators

After selection, unsafe permission, initialization and Move state, actual Loans and lifetimes, required accessor access, write capability, and other control-flow or ownership conditions are checked. Static Type and declaration permissions needed for adaptation were checked earlier; flow-dependent failures never change the selected overload.

```kimi
unsafe func inspect(value?: i32) -> () => ()
func inspect(value?: ref/i32) -> () => ()
let number: i32 = 1
inspect(number) // Select Exact i32, then reject without an Unsafe Block.
```

Adding a better overload can invalidate existing calls, even if the new overload then fails usage legality. A failed Move, Loan or Property permission check cannot retry with a borrow, a getter or a same-name declaration.

Operator operands keep the fixed syntax, evaluation order and permitted adaptations. Built-in operations and the comparison Contract mappings of §13.4.1 define the available operator candidates; there are no extension operator candidates in this revision. User-defined arithmetic, general indexer declarations and additional ambiguity-resolution syntax remain deferred, and ordinary lookup must not invent them.

**Diagnostics** distinguish undefined, wrong-role and inaccessible names, path or value-kind conflicts, missing receivers, Type-argument or argument mismatches, no applicable overload, ambiguity, inference boundaries, dependency cycles, declaration errors and usage errors. Ambiguity diagnostics show the candidate Signatures and declaration locations and keep useful rejection reasons without dumping every tentative error. Exhausting a resource limit is distinct from language ambiguity or mismatch; it must request annotations or smaller expressions and never chooses the first candidate.

## 10.7. Callable signature compatibility

Callable variance uses the static Type relations of [the relation table](03-types-and-values.md#38-type-relations-and-expression-operations); common Function Type conversion and receiver acquisition are separate operations.

Common Function Type conversion and `Callable<r, S>` use one rule. For implementation `(A1, ..., An) -> R` and required `(P1, ..., Pn) -> Q`, the arity must be equal, `Pi <: Ai` must hold at every position, and `R <: Q` must hold. Here `<:` means normalized complete-Type identity or an already defined subtype relation, keeping Semantics and Origins. Parameter labels, name-omission permissions and defaults cannot bridge a mismatch.

Compare parameter/result Origins and Loans for every admitted call using [canonical Origin contracts](15-ownership-and-lifetime-analysis.md#1537-canonical-contracts-and-verification). Required quantifiers are rigid; only the implementation's call-time Origins are inferred. Fixed captures cannot become fresh quantifiers. Compatibility inserts no Borrow/Reborrow, dereference, numeric or user conversion, or argument/result erasure, and never reinfers committed Types. Covariant Origin shortening remains valid, while exclusive Core invariance and result Loans remain mandatory. Receiver adaptation is separate and adds no argument conversions.

Implicit erasure applies only after the expected common Function Type is fixed. No overload ordering is defined between a concrete direct match and an erasure conversion; use an annotation or typed intermediate where that comparison would be needed. Allocation cost never ranks candidates, and a receiver, Copy, Owned or Loan failure cannot reopen selection.

## 10.8. Generic argument inference

The additional [fixed-array inference rules](04-arrays-indexing-and-slices.md#44-function-length-parameters) apply to lengths and literal element counts. Lengths are kept alongside Type, Semantics and Origin bindings within each candidate, and results are independent of argument traversal order.

Within each candidate, explicit arguments bind first. Otherwise, structural Type, Semantics and Origin constraints are collected together from the receiver and the independently typable arguments; the first input is not fixed with later inputs adapted to it. An independently known expected result is used only for still-unbound parts, without changing Types or Semantics established by inputs. The nested-expression boundaries and literal fitting apply, and literal defaults are used only after all other evidence has been processed.

An unbound `s` is inferred directly from the source's outer Semantics. No implicit Borrow, Reborrow or other conversion is searched to find a common Semantics: `owner` and `ref` evidence for the same `s` conflict. Once a target Type is fixed, including by explicit arguments, normal adaptation is checked separately. Origin inference uses the [limited principal-solution rules](15-ownership-and-lifetime-analysis.md#1536-limited-origin-inference).

Structural, Semantics and Origin constraints are solved to a fixed point, and every required slot must be resolved uniquely and consistently. An occurs-check rejects infinite substitutions such as `X = ref/X`; nominal recursive Types instead require a valid layout. Cycles supply no result. Unresolved work is kept only for an identified dependency that can resolve by its deadline.

Argument or candidate traversal order, arbitrary conversion chains, common-base search and Constraint strength never choose a solution. Constraint substitution uses the same binding table as Type expressions: `s is reference` checks the Semantics projection, and the pair's `T is C` checks its target projection and that requirement's role; an ordinary `T` keeps its complete Semantics. Then fixed-target argument adaptation, substituted Constraints and expected-result compatibility are checked; flow-dependent acquisition, Loans and cleanup follow selection.

## 10.9. Inference and operation design boundaries

The following are not specified in this revision, and implementations must not invent them through broader search:

- General Const arguments beyond [function lengths](04-arrays-indexing-and-slices.md#44-function-length-parameters), standalone Semantics slots, partial, default or variadic generic arguments, and partial or conditional specialization.
- Implicit argument or receiver adaptations beyond the defined applicability table, and exact contextual-binding boundaries for additional accessor or function forms. The explicit Borrow table adds no implicit overload preferences.
- Operator and indexer candidate collection and explicit selection syntax. Constructor collection is defined under [constructors](06-declarations-and-containers.md#623-constructors); external/internal parameter-name syntax, including `?`, is defined in §7.2.
