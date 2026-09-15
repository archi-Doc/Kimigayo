# 10. Overload resolution and inference

[Specification index](../SPEC.md)

Use the distinct relations in [Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations): argument adaptation, expected-result compatibility, and acquisition legality are separate judgments. A successful subtype proof does not select or authorize a value operation.

## 10.1. Candidate applicability

Check each declaration in the committed function group against the following requirements, subject to §10.5's shared-expectation and body-checking boundaries. These steps do not authorize checking a nested call or anonymous body separately for each candidate:

1. Validate explicit type-argument count and kinds.
2. Match positional and named arguments and record omitted defaults.
3. Infer type arguments from the receiver and explicit arguments.
4. Use an independently known expected result Type to fill remaining type arguments, without changing those already fixed.
5. Check permitted argument adaptations, Function Types, Constraints, and any conditional-member premises (§8.4.8) after substitution and before Best Candidate comparison.
6. Reject instantiated result Types incompatible with the expected result, if present.

Zero applicable candidates is an error. Candidate checking records plans; it does not execute or commit runtime Copy/Move, Loans, or defaults. Errors in declarations, such as unknown Types, malformed Constraints, or duplicate Signatures, remain declaration errors even when another candidate succeeds.

Positional arguments precede named arguments and bind parameters in order. Named arguments use external names, may be reordered, and cannot bind a parameter twice. Reject unknown labels, excess positional arguments, and missing required arguments. Evaluate explicit arguments in source order, then omitted defaults in parameter order; defaults follow [declaration-site rules](07-functions-and-callable-values.md#72-parameter-names-and-defaults) and supply no generic-inference evidence. Function-value calls supply every argument positionally.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // Valid; evaluate by before value.
scale(3, value: 4)     // Error: value supplied twice.
scale(3, factor: 4)    // Error: factor is an internal name.
```

## 10.2. Argument adaptation and literals

Compare adaptations in this order, best first:

| Class | Meaning |
| --- | --- |
| Exact | Normalized Type compatibility requiring no adaptation operation, including no borrow/reborrow |
| Literal fitting | Directly fit an unresolved literal to the candidate's Type |
| Same-semantics reborrow | Reborrow while preserving the input Type Semantics |
| Cross-semantics borrow/reborrow | Another permitted borrow or reborrow |

Exact describes Type adaptation, not value transfer: an Exact by-value argument still Copies or Moves under [Copy and Move](03-types-and-values.md#35-copy-and-move). Copy versus Move adds no ranking preference. Origin subtyping that needs no value operation remains permitted.

The initial borrow adaptations are listed below. In the owner-Place and owner-temporary rows `T` has owner Semantics; in value-reborrow rows it is the complete immediate Referent Type. Adding a layer around an existing reference or object handle requires a fully specified explicit [storage-borrow target](13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow); it is not an additional implicit argument adaptation.

| Input | Expected | Operation/class |
| --- | --- | --- |
| Readable `T` place | `ref/T` | Shared borrow / cross |
| Owner `T` temporary | `ref/T` | Materialize once and shared-borrow / cross |
| Exclusively writable `T` place | `uniq/T` | Exclusive borrow / cross |
| `uniq/T` | `uniq/T` | Call reborrow / same |
| `uniq/T` | `ref/T` | Shared reborrow / cross |
| Accessible `obj/T` place | `objref/T` | Shared object borrow / cross |
| Exclusively writable `obj/T` place | `objuniq/T` | Exclusive object borrow / cross |
| `objuniq/T` | `objuniq/T` | Object call reborrow / same |
| `objuniq/T` | `objref/T` | Shared object reborrow / cross |

A required exclusive reborrow is not Exact even when the written Types match. Check Type/declaration permissions and place-versus-temporary form during applicability; check flow-dependent initialization and active Loan conflicts after selection. Borrow adaptations neither extend lifetimes nor duplicate ownership. Do not infer further rc/arc, exclusive-temporary or outer-layer borrow adaptations from this table.

[Inherited receiver projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection) defines its member-only operations and rankings separately; those rows do not apply to ordinary arguments or unbound calls.

Fixed-expectation [common function conversion](07-functions-and-callable-values.md#764-function-references-and-common-type-conversion) is handled separately and adds no rank to this table. Do not add implicit object upcasts, numeric-width, signedness, integer/float, or user-defined conversions, or unlimited dereference/conversion chains. Raw dereference is explicit. Existing Never and Origin rules remain Type rules, without new overload priorities.

Untyped integer literals fit representable candidate integer Types directly; floating literals follow the numeric rules. Do not default to `i32`/`f64` before fitting, prefer narrower widths, or break overload ambiguity using defaults. Outside candidate comparison, independent expressions without an expected Type use the ordinary numeric defaults. Generic inference processes receiver, other-argument, and known-result constraints before defaulting. `null`, empty collections, and untyped functions gain no universal fallback Type.

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(1)      // Error: both integer Types fit.
let x = 1      // Independently defaults to i32.
choose(x)      // Exact i32.
choose(1@i64)  // Exact i64.
```

An owner temporary can be shared-borrowed for **any** ref/T argument, not only collection keys. Evaluate once and materialize a Temporary Place. A known source Type can infer T as for an owner Place; the target need not already be fixed. For an unfitted literal, process receiver, other-argument and known-result constraints, then candidate fitting and ordinary value defaults as above. Rank the result as cross-semantics borrowing, below Exact and direct literal fitting; defaults do not break ties.

Existing reference Copy/Reborrow takes priority over adding a layer: this rule adds no implicit uniq temporary borrow, outer borrow of an object handle, or outer layer around an existing reference. For example, a key K = ref/X needs an explicit @ref/ref/X to borrow its slot.

The materialized owner lasts through the normal outermost expression/condition/match temporary boundary (§3.6), at least through the call. Do not shorten it to call end or extend it to retain a returned borrow. On escape failure, identify the borrow, temporary end and required use; suggest a named local only when its Type/Origin constraints can work.

```kimi
func inspect<T>(value: ref/T) -> () => ()
func makeValue() -> i64 => 1
inspect(makeValue()) // Infer T = i64 and borrow the temporary.
inspect(1) // T defaults to i32 after constraints.

func chooseBorrow(value: ref/i32) -> () => ()
func chooseBorrow(value: ref/i64) -> () => ()
chooseBorrow(1) // Error: both fit; default i32 does not break the tie.
```

## 10.3. Expected results

This is the static expected-result judgment in [the relation table](03-types-and-values.md#38-type-relations-and-expression-operations), not the implicit-expression-adaptation judgment.

An expected result may complete inference and exclude otherwise applicable candidates. Compatibility requires normalized Type identity or a defined subtype relation without additional value operations. Instantiate and check Origins, including permitted covariant shortening. Do not insert a new borrow/reborrow, dereference, numeric conversion, or user conversion to retain a candidate. Do not retype a function's body literal to change its established return Type.

Expected results do not rank candidates by result-conversion quality. Result Loan/Origin propagation and Copy/Move still apply. An expectation comes from a surrounding annotation, fixed parameter, or declared result, subject to §10.5; it cannot circularly select its own source candidate. Discarding a call supplies no expected Unit Type. Constructs with Unit-fixed Target Result Types follow §14.2; their directly discarded calls still receive no Unit expectation.

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // Selects fetch(i32) by result compatibility.
fetch(1)                   // Error: discarding leaves both candidates.
```

These declarations have distinct parameter Signatures. Declarations differing only in return Type are invalid regardless of call-site expectations. A candidate returning `T` cannot survive an expected `ref/T` by borrowing its result; nor can `uniq/T` become `ref/T` by a newly inserted reborrow.

## 10.4. Best candidate

Pairwise comparison yields better, worse, equivalent, or incomparable. **Proceed to the next step only for equivalent candidates.** Select a candidate only if it is better than every other applicable candidate:

1. Compare adaptation quality for the receiver and each explicit source argument. A dominates B only if it is no worse everywhere and better somewhere. All equal proceeds; opposing advantages are incomparable. Match named arguments by the same source expression, not candidate parameter order. Exclude defaults and never sum numeric costs.
2. Compare substituted parameter Types at the same positions. A dominates B if every Type is equal or a defined subtype and at least one is a strict subtype. All equal proceeds; unrelated Types or opposing subtype advantages are incomparable.
3. Prefer a function with no generic parameters of its own, including length parameters. A generic enclosing Type alone does not make the function generic.
4. Prefer fewer defaults used by this call.
5. Otherwise report ambiguity.

Do not rank numeric Types by width, Constraints by strength or clause count, or generic declarations by general pattern partial ordering. Do not invent `uniq/T <: ref/T` from the ability to reborrow. Incomparability at an earlier step cannot be rescued by nongeneric status or fewer defaults; declaration, file, alias, and name order never break ties.

```kimi
func inspect(value: ref/i32) -> () => ()
func inspect(value: uniq/i32) -> () => ()
var x: i32 = 0
inspect(x)      // Error: both cross-semantics borrows; Types incomparable.
inspect(x@ref)  // Shared candidate: Exact.
inspect(x@uniq) // Exclusive candidate: same-semantics beats cross-semantics.
let action: (uniq/i32) -> () = inspect
```

The exclusive candidate wins for an exclusive input because its Semantics match, not because exclusivity is stronger. Two candidates `(i32, ref/i32)` and `(ref/i32, i32)` are incomparable for two `i32` locals. Likewise, `f<T>(T)` and `f<U>(Box<U>)` remain tied for `Box<i32>` when substitution makes both parameter Types equal and later steps tie.

These rules deliberately leave owner-to-`ref`/`uniq` overloads ambiguous. Any preference would require a language revision. Field Move eligibility and its generic limits follow [Field Move](11-properties.md#1112-move-paths-and-inherited-fields).

## 10.5. Inference boundaries and specialization

Fix local Types at declaration; later uses cannot infer backward. [Named functions](07-functions-and-callable-values.md#7-functions-and-callable-values), including local functions, methods, and Contract requirements, have an explicit result or Unit, independent of access and body form. Neither bodies nor callers infer their signatures. Check [API accessibility](09-names-signatures-and-access.md#932-api-signature-accessibility); substituting a written generic result does not reanalyze the body. Local binding and [anonymous-function inference](07-functions-and-callable-values.md#761-syntax-and-inference) remain available.

Explicit Type arguments follow the [slot rules](08-generics-constraints-and-contracts.md#81-generic-type-parameters). Omitted defaults do not infer generic arguments. Explicit specializations do not participate in inference or overload applicability; select an implementation only after the original declaration and its static generic arguments are determined.

Generic inference and substitution use the [complete-Type slots and projections](08-generics-constraints-and-contracts.md#81-generic-type-parameters), preserving all bound Origins and inferred Loan requirements, including nested dependencies. A surrounding borrow such as `ref/T` retains `T`'s internal dependencies alongside its own Origin and Loan. Substitution alone creates no Borrow/Reborrow, releases no Loan, and extends no lifetime; actual call-site checks follow [Ownership and Origin rules](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis).

Bidirectional checking supports literals, function references, anonymous functions, and expressions directly checkable against a candidate Type. It does not search combinations by rerunning an inner overload for every outer candidate in `f(g(x))`:

- First process independently typable arguments and explicit Type information.
- An inner expression may use an expected Type shared by all remaining outer candidates.
- If one outer candidate is already determined, use its expectation without reviving eliminated candidates after failure.
- Otherwise require an annotation, explicit type arguments, or a typed intermediate binding.

```kimi
func inner(value: i32) -> i32 => value
func inner(value: i64) -> i64 => value
func outer(value: i32) -> () => ()
func outer(value: i64) -> () => ()
outer(inner(1))                    // Error: would require nested search.
let intermediate: i64 = inner(1)  // Expected result fixes the inner call.
outer(intermediate)
```

Resolve function references using ordinary evidence, including explicit generics or a fixed expected callable signature. A unique declaration needs no expected Type; an unresolved overload set is not a value. Function values have no labels/defaults and cannot name unsafe functions or deinit. Conformance failure cannot change the chosen overload or capture mode.

**Anonymous body context.** Process explicit Types, independently typable arguments, and generic constraints before checking the body, independent of argument order. Arity and explicit Types may filter candidates. Use the written signature, a common remaining expectation, or an already selected candidate's signature; `Callable<r, S>` may guide parameters while F retains its concrete Closure Type. If unresolved candidates cannot provide the needed expectation, require an annotation. Do not inspect return expressions to select candidates, retry bodies/captures across candidates, or infer parameters from later uses.

If the normalized return Type is Unit before body checking, discard the single-item expression (§7.1). Otherwise use Value Context, including return inference. Unit inferred from another argument before body checking is allowed; its spelling or origin does not matter. Do not reinterpret the body/captures after later Unit inference or generic instantiation. A standalone lambda without an expectation can infer its return; an already typed function value cannot erase its return to Unit. No overload preference between using and discarding lambda results is added.

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

After inference, apply the [limited proof system](08-generics-constraints-and-contracts.md#87-constraint-proof-system): Proven satisfies a requirement, Refuted rejects it, and Error diagnoses invalid/contradictory evidence. Unknown may retain only a legitimate dependency resolvable by its deadline; it proves neither applicability nor negation. Generic capabilities must be proved at definition acceptance (§8.10). Bind nondependent Names at the definition and use declared Constraints; failure to prove `T is C` does not prove `T is not C`. Deferred members retain the [definition environment](18-modules-and-dependencies.md#18-modules-and-dependencies), never caller imports. Prove all necessary Constraints before concrete finalization. No arbitrary theorem proving, Type enumeration, or constraint-strength ranking is allowed.

Environment-selected membership follows §19.4: excluded declarations do not merge or enter candidate sets. Conditional-conformance members (§8.4.8) remain in ordinary lookup; their published conditions affect applicability, not lookup stopping or syntax selection. Preserve each defining generic environment.

## 10.6. Usage legality and operators

After selection, check unsafe permission, initialization and Move state, actual Loans and lifetimes, required accessor access, write capability, and other control-flow or ownership conditions. Static Type and declaration permissions needed for adaptation are checked earlier; flow-dependent failures never change the selected overload.

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: ref/i32) -> () => ()
let number: i32 = 1
inspect(number) // Select Exact i32, then reject without an Unsafe Block.
```

Adding a better overload can invalidate existing calls even if it later fails Usage Legality. Failed Move, Loan, or Property permission checks cannot retry a borrow, getter, or same-name declaration.

Operator operands preserve the fixed syntax, evaluation order, and permitted adaptations. Built-in operations and the comparison Contract mappings in §13.4.1 define the available operator candidates. No extension operator candidates exist in this revision. User-defined arithmetic, general indexer declarations, and additional ambiguity-resolution syntax remain deferred; ordinary lookup must not invent them.

Diagnostics distinguish undefined/wrong-role/inaccessible names, path or value-kind conflicts, missing receivers, type-argument or argument mismatch, no applicable overload, ambiguity, inference boundaries, dependency cycles, declaration errors, and usage errors. Show candidate Signatures and declaration locations for ambiguity; retain useful rejection reasons without dumping every tentative error. Resource-limit exhaustion is separate from language ambiguity or mismatch and must request annotations or smaller expressions, never choose the first candidate.

## 10.7. Callable signature compatibility

Callable variance uses the static Type relations in [the relation table](03-types-and-values.md#38-type-relations-and-expression-operations); common Function Type conversion and receiver acquisition are separate operations.

Common Function Type conversion and `Callable<r, S>` use one rule. For implementation `(A1, ..., An) -> R` and required `(P1, ..., Pn) -> Q`, require equal arity, `Pi <: Ai` at every position, and `R <: Q`. Here `<:` means normalized complete-Type identity or an already defined subtype relation, retaining Semantics and Origins. Parameter labels, defaults, and optionality cannot bridge a mismatch.

Compare parameter/result Origins and Loans for every admitted call; quantified Origins correspond by binder, not spelling. Fixed captures cannot become fresh quantifiers. Compatibility inserts no Borrow/Reborrow, dereference, numeric/user conversion, or argument/result erasure and never reinfers committed Types. Covariant Origin shortening remains valid; exclusive Core invariance and result Loans remain mandatory. Receiver adaptation is separate and adds no argument conversions.

Implicit erasure applies only after the expected common Function Type is fixed. No overload ordering between a concrete direct match and an erasure conversion is defined; use an annotation or typed intermediate when that comparison would be needed. Allocation cost never ranks candidates. Receiver, Copy, Owned, or Loan failure cannot reopen selection.

## 10.8. Generic argument inference

Apply the additional [fixed-array inference rules](04-arrays-indexing-and-slices.md#44-function-length-parameters) to lengths and literal element counts. Keep lengths alongside Type, Semantics, and Origin bindings within each candidate, with results independent of argument traversal order.

Within each candidate, bind explicit arguments first. Otherwise collect structural Type, Semantics, and Origin constraints from the receiver and independently typable arguments together; do not fix the first input and adapt later inputs to it. Use an independently known expected result only for still-unbound parts, without changing input-established Types or Semantics. Apply the existing nested-expression boundaries and literal fitting, using literal defaults only after other evidence has been processed.

Infer unbound s directly from the source's outer Semantics. Do not search implicit Borrow/Reborrow or other conversions for a common Semantics: owner and ref evidence for the same s conflicts. Once a target Type is fixed, including by explicit arguments, check normal adaptation separately. Origin inference uses the [limited principal-solution rules](15-ownership-and-lifetime-analysis.md#1534-generic-origin-inference).

Solve structural, Semantics, and Origin constraints to a fixed point, resolving every required slot uniquely and consistently. An occurs-check rejects infinite substitutions such as X = ref/X; nominal recursive Types instead require valid layout. Cycles supply no result. Retain unresolved work only for an identified dependency that can resolve by its deadline.

Do not use argument/candidate traversal order, arbitrary conversion chains, common-base search, or Constraint strength to choose a solution. Constraint substitution uses the same binding table as Type expressions: `s is reference` checks the Semantics projection; the pair's `T is C` checks its target projection and that requirement's role. Ordinary T retains its complete Semantics. Then check fixed-target argument adaptation, substituted Constraints, and expected-result compatibility; flow-dependent acquisition, Loans, and cleanup follow selection.

## 10.9. Inference and operation design boundaries

The following boundaries remain separately specified. Implementations must not invent them through broader search:

- General Const arguments beyond [function lengths](04-arrays-indexing-and-slices.md#44-function-length-parameters), standalone Semantics slots, partial/default/variadic generic arguments, and partial/conditional specialization are not introduced.
- Additional implicit argument/receiver adaptations beyond the defined applicability table; exact contextual-binding boundaries for additional accessor/function forms. The explicit Borrow table does not add implicit overload preferences.
- Operator/indexer candidate collection and explicit selection syntax; combining optional `?` with external/internal parameter-name syntax. Constructor collection is defined under [constructors](06-declarations-and-containers.md#623-constructors).
