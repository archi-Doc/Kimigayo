# Specification change proposal: effect bounds on Callable Constraints

Date: 2026-10-04

Status: Proposal. Adoption is undecided; nothing is integrated into the specification or implemented. It is a prerequisite of stage 3 of `2026-10-04 Async Tasks.md`.

References: `SPEC §n` is the language specification and a bare `§n` this proposal.

## 1. Summary

A Callable Constraint may declare the existing effect bounds `confined` and `preserves results`:

```kimi
group Settings
    public var greeting: string = "hello"

func applyLogged<F>(transform: ref/F, value: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    let view = Settings.greeting@ref        // A Loan on a mutable static.
    let result = transform(value)           // Valid only because of the bound.
    use(view)
    return result
```

- **Inside the declaration,** a call through `F` uses the bound as an available bound, as a generic requirement call does (SPEC §8.4.10.4).
- **At each binding,** the Type bound to `F` satisfies the Constraint only if a call of it satisfies the effect bounds.
- **Unchanged:** the vocabulary stays closed, and Function Types, ordinary functions and Property requirements still declare no bounds of their own.

## 2. Problems in the current specification

1. **Callbacks have unknown effects.** Effect items appear only in Contracts (SPEC §8.4.10.1), so a call through `F is Callable<r, S>` in a generic body has no available bound, and its environment effects are unknown.
   - Generic code cannot hold a Loan on a mutable static across a callback call (SPEC §15.6.4).
   - A Contract implementation that calls a generic callback cannot satisfy `confined` or `preserves results`, because unknown effects count as conflicts (SPEC §8.4.5).
2. **An API cannot require a callback property.** Instantiation adds no semantic use conditions (SPEC §8.10), so a requirement such as "the callback must be `confined`" would have to be part of the declared Constraint, and no Constraint can state it today.
   - Stage 3 of `2026-10-04 Async Tasks.md` needs exactly this. `Async.offload` must reject children that obtain authority from the environment, such as direct access to mutable statics or foreign calls.
   - A static borrow that a child receives through a capture is authority from an input (SPEC §8.4.10.2). Rule 2 of that proposal rejects it instead, as a static Loan held across the task call.

## 3. Changes

### 3.1. Declaration (SPEC §8.4.10.1)

```text
ConstraintClause := ConstraintSubject "is" IsRequirement CallableEffects?
CallableEffects  := IndentedList<EffectClause>
```

- **Form.** A `CallableEffects` list is valid only when the whole `IsRequirement` is one `CallableRequirement`, without `and`, `or`, `not` or parentheses. Other requirements on the same subject are written as separate clauses.
- **Positions.** The list is allowed in function Constraint regions (conditional-member premises included), struct and enum Constraint regions, and function-requirement Constraint regions. It is not allowed in Contract-wide clauses, associated-Type declarations and specifications, or conditional-conformance conditions.
- **Requirement regions.** In a function requirement's Constraint region, an `EffectClause` at the region's own level still declares the requirement's bound; one indented under a Callable Constraint declares the bound of that Constraint:

```kimi
contract Mapper
    func map<F>(self: uniq/Self, transform: ref/F) -> i32
        F is Callable<(i32) -> i32>
            effect confined         // Bound of the Callable Constraint on F.
        effect confined             // Bound of the requirement Mapper.map.
```

- **Body prefix.** In a body's leading Constraint prefix (SPEC §7.4), the list belongs to its clause and does not end the prefix.
- **Count.** Each bound kind appears at most once in one Constraint; one Constraint may declare both `confined` and `preserves results`.
- **Eligibility.** `preserves results` requires the receiver `ref` or `uniq`. Results never borrow the callable's environment (SPEC §8.6), so the eligibility condition of SPEC §8.4.10.3 reduces to this.
- **Placement rule.** The SPEC §8.4.10.1 bullet "Effect items appear only in Contracts. Implementations, ordinary functions, Property requirements, Function Types and Callable Constraints accept none, and an implementation can neither add nor remove a bound." becomes: "Effect specifications appear only in Contracts, and effect clauses only in requirement Constraint regions and under Callable Constraints. Implementations, ordinary functions, Property requirements and Function Types declare no bounds of their own, and an implementation can neither add nor remove a requirement's bound."

### 3.2. Meaning

A Type `X` bound to `F` satisfies `F is Callable<r, S>` with effect bounds `B` when `X is Callable<r, S>` holds and a call of `X` with receiver access `r` satisfies every bound in `B`:

- **`confined`.** The call's transitive effect summary (SPEC §8.4.5) contains no environment effect and no unclassifiable effect (SPEC §8.4.10.2).
  - The call includes the receiver acquisition under `r`. With `owner`, it also includes destroying the remaining acquired storage when the call completes (SPEC §8.6, §16.4).
  - A Closure's captures are reached through its receiver, which is an input, so accessing them is no environment effect.
  - The bound does not cover destroying a value of `F` outside a call.
- **`preserves results`.** The call's effects conflict with no Loan kept by a result of an earlier call of the same callable value (SPEC §8.4.10.3, with "requirement" read as the callable).

### 3.3. Checking at a binding (SPEC §8.4.10.4)

- **Not part of applicability.** Candidate applicability (SPEC §10.1 step 5), conditional-member premises (SPEC §7.4) and the proof judgment of SPEC §8.7 judge a bounded Callable Constraint without its bounds. Each bound is an obligation of the selected call or function reference (SPEC §10.5), or of the Type formation for a Type Constraint. It is checked after selection, against the completed summary of the bound Type, once the fixed point of SPEC §15.6.4 is reached. A failed bound never excludes a candidate or causes reselection, as for unsafe calls (SPEC §7.5) and ObjectCallCompatible (SPEC §12.4.4).
- **Where the obligation arises.**
  - For a call, it is part of the call's declared contract (SPEC §8.10, "Check the call's declared contract").
  - For a function reference, it arises for the reference's Type arguments, whether explicit or inferred from its expected signature (SPEC §10.5).
  - For a Type, it arises where its Type arguments are checked (SPEC §8.1.3).
  - In every case it is discharged with the other bound checks after the fixed point, as above.
- **What.** For each kind of Type bound to `F`:

| Type bound to `F` | Check |
| --- | --- |
| Function Item of a declaration | Its transitive effect summary (SPEC §8.4.5, §15.6.4): computed in the same build, otherwise the published summary (SPEC §18.7.2), which records the environment-effect kinds of SPEC §8.4.10.2. Without one, the effects are unclassifiable and the bound fails |
| Function Item of a requirement through a constrained Type (SPEC §8.4.6) | For `confined`, the bounds available for that requirement (SPEC §8.4.10.4). `preserves results` is never satisfied: the requirement's bound covers only earlier results on the same receiver value (SPEC §8.4.10.3), whereas the Callable bound covers every earlier call of the callable value |
| Concrete Closure | The summary of its body. With receiver `owner`, also the destruction of its remaining environment, which the call performs when a Shared or Exclusive body completes (SPEC §8.6, §16.4) |
| Abstract Type (a generic parameter or an associated-Type projection) | The premise that is exactly `G is Callable<r, S>` (SPEC §8.7, exact assumption) proves the Constraint, and each required bound is declared by that premise or by a premise `G is Callable<r', S>` whose receiver `r'` is `r` or stronger (§3.4). A premise with a weaker receiver does not qualify: a `ref` or `uniq` bound does not cover the destruction of an `owner` call |
| Common Function Type | Never satisfies a bounded Constraint, because erasure keeps no bounds |

- **Generic bodies.** In a generic body, each summary is the one verified under the enclosing declaration's premises, never an instantiated one (SPEC §8.10).
- **Proofs.** Bounds are not part of the proposition that SPEC §8.7 proves. Where a bounded Callable Constraint must be proven from premises (at a generic binding, or for an implementation's Constraints under SPEC §8.4.5), each of its bounds must be declared by a premise with the same subject and signature and the same or a stronger receiver (§3.4).

### 3.4. Use inside the declaration (SPEC §8.4.10.4)

- **Available bounds.** A constraint-based call selected under receiver `r` and signature `S` (SPEC §8.6) may use the bounds of every premise `F is Callable<r', S>` whose receiver `r'` is `r` or stronger (in the order `ref`, `uniq`, `owner`). Each such premise is checked against the same body, and an `owner` bound also covers destruction.
- **Call effects.** Rules 1–3 of "Requirement call effects" then apply unchanged:
  1. effects through inputs, including the callable receiver acquired under `r`;
  2. no environment effects with `confined`;
  3. with `preserves results`, the Loans of earlier results of the same callable value are excluded.
- **Without a bound,** the call keeps unknown environment effects, as today.
- **Composition (new).** A call of a declaration whose body calls a Callable-constrained parameter composes those calls through the actual argument: with the concrete summary of a Function Item or Closure, or with the available bounds of an abstract Type. The callee's published summary records those calls against the parameter, not as unknown effects (§4).

With `applyLogged` of §1:

```kimi
group Metrics
    public var calls: isize = 0

let double = func [] (value: i32) -> i32 => value * 2
let counted = func [] (value: i32) -> i32
    Metrics.calls += 1
    return value

_ = applyLogged(double@ref, 1)              // Valid.
_ = applyLogged(counted@ref, 1)             // Error: counted writes the mutable static Metrics.calls.
```

Without `effect confined`, the call `transform(value)` in `applyLogged` would conflict with `view`.

**Task calls.** A bound never exempts a task call: rule 2 of `2026-10-04 Async Tasks.md` compares a task call as having unknown environment effects whatever its bounds. This note enters the specification with that proposal's Chapter 24 and cites its rule 2.

### 3.5. Publication and compatibility (SPEC §18.7)

- Bounds belong to the declaration's public contract and to its verified semantic record (SPEC §18.7.1).
- **On a function,** adding a bound restricts its callers. Removing one admits more callers; the body is then re-verified without the bound.
- **On a Type,** adding a bound invalidates every formation of the Type with a violating callable.
- **On a function requirement,** adding a bound restricts generic callers and relaxes implementations. Removing one makes every implementation that declares it incompatible (SPEC §8.4.5).
- STYLE §2.2 narrows its existing rule to requirement bounds and gains: declare a Callable bound only where the body relies on it; adding one restricts callers and removing one admits more, so treat both as API changes.

### 3.6. Diagnostics (SPEC §8.4.10.6)

| Problem | Code |
| --- | --- |
| An effect item outside a Contract that is not under a Callable Constraint; an effect list under a requirement that is not one `CallableRequirement` (another atom or a combined requirement) or in an excluded position; a bound declared twice in one Constraint; `preserves results` with the receiver `owner` | `InvalidEffectBound_Kd` (existing) |
| A binding whose call violates a bound | `UnsatisfiedEffectBound_Kd` (new, Language) |
| An implementation's Callable Constraint declares a bound that no requirement or conformance premise declares | `IncompatibleContractImplementation_Kd` (existing, SPEC §8.4.5 Constraints row) |
| A constraint-based call whose unknown environment effects may conflict with an active static Loan | `ComparisonLoanConflict_Kd` (existing in the implementation; named for the static call-effect comparison of SPEC §15.6.4 by this change or by `2026-10-04 Async Tasks.md` §11, whichever is integrated first) |
| An effect through the inputs of a constraint-based call that may conflict with a Loan kept by an earlier result of the same callable | `CallEffectConflict_Kd` (existing) |

- **`InvalidEffectBound_Kd`.**
  - Message: "The effect item declares no bound: a bound appears once, either in a Contract for one function requirement of that Contract or an ancestor, or indented under a single Callable Constraint".
  - Reason "an effect item outside a Contract" becomes "an effect item outside a Contract or a Callable Constraint". The Note becomes "Only a Contract or a Callable Constraint declares bounds; {owner} declares no bound of its own, and an implementation can neither add nor remove a requirement's bound". SPEC §8.4.10.6 changes in the same way.
  - Related location: the Constraint that carries the list when there is one, otherwise the target requirement when one is identified (SPEC §8.4.10.6); none when neither exists.
- **`UnsatisfiedEffectBound_Kd`.** Message: "The callable given here does not satisfy an effect bound of its Callable Constraint". It is an Ownership-phase check after selection (§3.3), so it is neither `UnsatisfiedConstraint_Kd` (a Refuted proof in Binding) nor `UnprovenConstraint_Kd`. An unknown effect counts as a violation (SPEC §8.4.5).
  - Primary location: the argument or Type argument.
  - Related locations: the bounded Constraint, and the first violating effect when the body is visible.
  - Reason: the bound, and whether the violation is definite (naming the kind of environment effect), an unknown effect, a premise that does not declare the bound, a requirement Function Item whose `preserves results` holds only per receiver, or a common Function Type, whose erasure keeps no bounds.
  - Advice: pass the state as an argument or capture it; for an abstract Type, declare the bound on the enclosing premise; where the declaration is editable, remove the bound, noting that the body relies on it.
- **`IncompatibleContractImplementation_Kd`.** The Reason names the bound and the requirement premise that lacks it.
- **`ComparisonLoanConflict_Kd`.** Primary location: the call. When the call is not a task call, Advice gains: declare `effect confined` on the Callable Constraint, where the declaration is editable and every caller's callable satisfies it. For a task call no bound resolves the conflict (§3.4), and this Advice is not offered.
- **`CallEffectConflict_Kd`.** Its message "This requirement call ..." becomes "This call through a requirement or a Callable Constraint ...". Advice suggests `preserves results` on the Callable Constraint, where the declaration is editable and every Type bound to it satisfies the bound, or ending the use of the held result before the call (SPEC §8.4.10.6).
- **Hover.** Hover uses the typed block of SPEC §8.4.10.6 and lists each contributing premise:

```text
Call: transform(value)
Callable: F is Callable<(i32) -> i32>
Available bound: confined
Declared by: applyLogged
Premise: F is Callable<(i32) -> i32> effect confined
```

## 4. Specification changes

| Location | Change |
| --- | --- |
| SPEC §8.4.10 (introduction) | Bounds may also be declared by Callable Constraints, for constraint-based calls. "A bound only obliges implementations" becomes "A bound only obliges implementations and, for a Callable Constraint, the Types bound to it" |
| SPEC §8.4.10.1 | Grammar, positions and the placement rule (§3.1) |
| SPEC §8.4.10.2, §8.4.10.3 | The meaning for a callable, including `owner` destruction (§3.2), and the eligibility of `preserves results` (§3.1) |
| SPEC §8.4.10.4 | Checking at a binding (§3.3) and use inside the declaration (§3.4) |
| SPEC §8.4.10.6 | Diagnostics and hover (§3.6) |
| SPEC §7.4, §8.4.1 | The effect list belongs to its Callable Constraint clause and does not end the body prefix |
| SPEC §8.6 | A cross-reference to bounded Callable Constraints |
| SPEC §8.7 | Bounds are not part of proven propositions; premises must declare the required bounds (§3.3) |
| SPEC §10.1 | Step 5 judges a bounded Callable Constraint without its bounds |
| SPEC §12.4.4.2, §15.6.4 | Constraint-based calls under a bounded Callable Constraint derive their effects from the available bounds, as generic and erased requirement calls do. Calls through a Callable-constrained parameter enter the callee's summary against that parameter and are composed through the actual argument at each call (§3.4) |
| SPEC §15.6.4, §23 | Unless already done by `2026-10-04 Async Tasks.md`, name `ComparisonLoanConflict_Kd` for the static call-effect comparison |
| SPEC §18.7 | Publication and compatibility (§3.5) |
| SPEC Appendix D | In the effect-bound row, remove "Callable Constraints" from the deferred forms, and change the status to "Not introduced; Contracts declare the closed bounds for function requirements, and Callable Constraints for constraint-based calls". Function Types, concrete functions and Property requirements stay deferred |
| SPEC Appendix F | `ConstraintClause` gains the optional `CallableEffects` list |
| `docs/STYLE.md` §2.2 | Narrow the existing rule to requirement bounds ("Declare a requirement effect bound only where callers need it ..."), and add: declare a Callable bound only where the body relies on it; adding one restricts callers and removing one admits more, so treat both as API changes |
| `docs/LIBRARY.md` | No change now. Existing APIs such as `Array.sort(by:)` keep their Constraints: their bodies hold no Loan across the callback that a bound would protect, each caller composes the callback call through the actual argument (§3.4), and adding a bound would only restrict callers. `Kimi.Async.offload` (stage 3) is the first user |

## 5. Implementation plan

1. **Parser.** Accept the indented effect list under a Callable Constraint, and record its range for diagnostics.
2. **Binding.** Store the bounds with the Constraint, check form, position and eligibility, and keep the bounds out of applicability and proofs (§3.3).
3. **Ownership.** Use the bounds as available bounds for constraint-based calls, and check each binding after selection against the completed summary of the bound Type.
4. **Services.** Diagnostics and hover as in §3.6.
5. **Tests.** A reproducer and a valid counterpart for each diagnostic:
   - static Loans across bounded and unbounded calls;
   - Closure and Function Item bindings, including a requirement's Function Item under `confined` and under `preserves results`;
   - generic function references whose `F` is bound explicitly, and from an expected signature;
   - a bounded Callable Constraint of a struct or enum, formed with a violating callable and through a generic Type argument with and without the premise;
   - an implementation's Callable Constraint that declares a bound its requirement premise lacks;
   - an `owner` Closure whose remaining capture's `drop` writes a mutable static;
   - generic pass-through with and without the premise, and with a premise of another receiver;
   - a common Function Type binding;
   - an anonymous function passed directly, and a recursive Function Item binding;
   - `preserves results` eligibility;
   - a `confined` Contract implementation that calls a bounded callback;
   - an effect list in each excluded position and under a combined requirement.

## 6. Rejected alternatives

| Alternative | Reason not chosen |
| --- | --- |
| Bounds on Function Types | Erased values would have to carry bounds in their Type identity and conversions; this stays deferred (SPEC Appendix D) |
| Inferring a declaration's bounds from its body | What callers must pass would depend on the declaration's private body (Principle 2). Checking a bound Type against its own summary is different: it uses the summaries that SPEC §15.6.4 already publishes for static call effects |
| Checking only at instantiation | Instantiation adds no semantic use conditions (SPEC §8.10), so the requirement must be declared |
| Bounds as part of applicability | Applicability is decided before anonymous bodies are checked and before the summary fixed point (SPEC §10.1, §10.5, §15.6.4); a bound would also make overload choice depend on effects |
| Reusing `UnsatisfiedConstraint_Kd` | It reports a Refuted proof in Binding with range-specific evidence; the bound check is a separate Ownership-phase requirement |
| A built-in `ConfinedCallable` Contract | It would duplicate `Callable` for each bound and combination (Principle 1) |
| New vocabulary (for example `pure`) | The vocabulary is closed; adding a bound kind requires its own specification change (SPEC §8.4.10.1) |
