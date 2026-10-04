# Specification change proposal: effect bounds on Callable Constraints

Date: 2026-10-04

Status: Proposal. Adoption is undecided; nothing is integrated into the specification or implemented. It is a prerequisite of stage 3 of `2026-10-04 Async Tasks.md`.

References: `SPEC §n` is the language specification and a bare `§n` this proposal.

## 1. Summary

A Callable Constraint may declare the existing effect bounds `confined` and `preserves results`:

```kimi
func applyTwice<F>(transform: ref/F, value: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    return transform(transform(value))
```

- **Inside the declaration,** a call through `F` uses the bound as an available bound, as a generic requirement call does (SPEC §8.4.10.4).
- **At each binding,** the bound Type satisfies the Constraint only if a call of it satisfies the bound.
- **Unchanged:** the vocabulary stays closed. Function Types, ordinary functions and Property requirements still accept no bounds.

## 2. Problems in the current specification

1. **Callbacks have unknown effects.** Effect items appear only in Contracts (SPEC §8.4.10.1), so a call through `F is Callable<r, S>` in a generic body has no available bound, and its environment effects are unknown.
   - Generic code cannot hold a Loan on a mutable static across a callback call (SPEC §15.6.4).
   - A Contract implementation that calls a generic callback cannot satisfy `confined` or `preserves results`, because unknown effects count as conflicts (SPEC §8.4.5).
2. **An API cannot require a callback property.** Instantiation adds no semantic use conditions (SPEC §8.10), so a requirement such as "the child must be `confined`" must be part of the declared Constraint. Stage 3 of `2026-10-04 Async Tasks.md` needs exactly this: `Async.offload` must reject children that reach mutable statics or foreign functions.

## 3. Changes

### 3.1. Declaration (SPEC §8.4.10.1)

```text
CallableConstraint := ConstraintSubject "is" CallableRequirement [IndentedList<EffectClause>]
```

- A Callable Constraint, wherever Constraints are written (function, Type and requirement Constraint regions), may be followed by an indented list of `EffectClause`s with the existing `EffectBound` vocabulary.
- A bound may appear once per Constraint.
- `preserves results` requires the receiver `ref` or `uniq`. Results never borrow the callable's environment (SPEC §8.6), so the eligibility condition of SPEC §8.4.10.3 reduces to this.
- The sentence "Effect items appear only in Contracts" becomes: "Effect items appear only in Contracts and Callable Constraints. Implementations, ordinary functions, Property requirements and Function Types accept none."

### 3.2. Meaning

`X` satisfies `F is Callable<r, S>` with bounds `B` when `X is Callable<r, S>` holds and a call of `X` with receiver access `r` satisfies every bound in `B`:

- **`confined`.** The call's transitive effect summary contains no environment effect and no unclassifiable effect (SPEC §8.4.10.2). The callable's environment is its receiver, so access to captured values is authority from an input.
- **`preserves results`.** The call's effects conflict with no Loan kept by a result of an earlier call of the same callable value (SPEC §8.4.10.3, with "requirement" read as the callable).

### 3.3. Checking at a binding (SPEC §8.4.10.4)

The bounded Constraint is part of the declared contract that instantiation checks (SPEC §8.10, "Check the call's declared contract"). For each kind of bound Type:

| Bound Type | Check |
| --- | --- |
| Function Item | Its transitive effect summary: inferred in the same build (SPEC §12.4.4.2), otherwise the published summary (SPEC §18.7.2) |
| Concrete Closure | The summary of its body |
| Generic parameter `G` of the enclosing declaration | A premise must prove `G is Callable<r', S'>` with at least the required bounds, where `r'` and `S'` satisfy the required receiver and signature |
| Common Function Type | Never satisfies a bounded Constraint, because erasure keeps no bounds |

The proof system (SPEC §8.7) gains one fact: a bounded Callable premise proves the same Constraint with any subset of its bounds.

### 3.4. Use inside the declaration (SPEC §8.4.10.4)

A constraint-based call of `F` (SPEC §8.6) may use the bounds of every premise that proves the selected Callable Constraint for `F`. Rules 1–3 of "Requirement call effects" then apply unchanged:

1. Effects through inputs, including the callable receiver acquired under `r`.
2. No environment effects with `confined`.
3. With `preserves results`, the Loans of earlier results of the same callable value are excluded.

Without a bound, the call keeps unknown environment effects, as today.

```kimi
group Settings
    public var greeting: string = "hello"

group Metrics
    public var calls: isize = 0

func applyLogged<F>(transform: ref/F, value: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    let view = Settings.greeting@ref        // A Loan on a mutable static.
    let result = transform(value)           // Valid: confined excludes environment effects.
    use(view)
    return result

let double = func [] (value: i32) -> i32 => value * 2
let counted = func [] (value: i32) -> i32
    Metrics.calls += 1
    return value

_ = applyLogged(double@ref, 1)              // Valid.
_ = applyLogged(counted@ref, 1)             // Error: counted writes the mutable static Metrics.calls.
```

Without `effect confined`, the call `transform(value)` in `applyLogged` would conflict with `view`.

**Task calls.** A bound never exempts a task call: rule 2 of `2026-10-04 Async Tasks.md` compares a task call as having unknown environment effects whatever its bounds.

### 3.5. Publication and compatibility (SPEC §18.7)

- Bounds belong to the declaration's Constraint contract and to its verified semantic record (SPEC §18.7.1).
- Adding a bound to a public declaration restricts its callers. Removing one only weakens what the body may assume and is re-verified with the body. STYLE §2.2 gains: declare a Callable bound only where the body relies on it.

### 3.6. Diagnostics (SPEC §8.4.10.6)

| Problem | Code | Details |
| --- | --- | --- |
| An effect item under a non-Callable Constraint, a bound declared twice in one Constraint, or `preserves results` with the receiver `owner` | `InvalidEffectBound_Kd` (existing) | Its message changes from "appears only in a Contract" to "appears only in a Contract or a Callable Constraint" |
| A binding whose call violates a bound | `UnsatisfiedConstraint_Kd` (existing) | Primary location: the argument or Type argument. Related locations: the bounded Constraint, and the first violating effect when the body is visible. Reason: the bound, and whether the violation is definite (naming the kind of environment effect) or an unknown effect; for a common Function Type, that erasure keeps no bounds. Advice: move the state into captures or parameters, or, where the declaration is editable, remove the bound, noting that the body relies on it |
| A call through `F` that may conflict with an active Loan | `CallEffectConflict_Kd` (existing) | Extends from requirement calls to constraint-based calls. Advice suggests a bound on the Callable Constraint |

Hover shows the available bound at a constraint-based call, with "Declared by" naming the Callable Constraint and "Premise" naming its subject.

## 4. Specification changes

| Location | Change |
| --- | --- |
| SPEC §8.4.10 | Bounds may also be declared by Callable Constraints, for constraint-based calls |
| SPEC §8.4.10.1 | Grammar and placement (§3.1) |
| SPEC §8.4.10.2, §8.4.10.3 | The meaning for a callable (§3.2), and eligibility of `preserves results` |
| SPEC §8.4.10.4 | Checking at a binding (§3.3) and use inside the declaration (§3.4) |
| SPEC §8.4.10.6 | Diagnostics and hover (§3.6) |
| SPEC §8.6 | A cross-reference to bounded Callable Constraints |
| SPEC §8.7 | A bounded premise proves any subset of its bounds |
| SPEC §18.7 | Publication and compatibility (§3.5) |
| SPEC Appendix D | Remove "Callable Constraints" from the row for effect bounds; Function Types, concrete functions and Property requirements stay deferred |
| SPEC Appendix F | The `CallableConstraint` production |
| `docs/STYLE.md` §2.2 | Declare a Callable bound only where the body relies on it |
| `docs/LIBRARY.md` | No change now. Existing APIs such as `Array.sort(by:)` keep their Constraints: a concrete instantiation already resolves exact effects (SPEC §8.10), and adding a bound would restrict callers. `Kimi.Async.offload` (stage 3) is the first user |

## 5. Implementation plan

1. **Parser.** Accept an indented effect list under a Callable Constraint, and record its range for diagnostics.
2. **Binding.** Store the bounds with the Constraint, check placement and eligibility, and extend the proof system with subset proofs.
3. **Ownership.** Use the bounds as available bounds for constraint-based calls, and check bindings against the summaries of Function Items and Closures.
4. **Services.** Diagnostics and hover as in §3.6.
5. **Tests.** A reproducer and a valid counterpart for each diagnostic: static Loans across bounded and unbounded calls, Closure and Function Item bindings, generic pass-through with and without the premise, a common Function Type binding, `preserves results` eligibility, and a `confined` Contract implementation that calls a bounded callback.

## 6. Design decisions

| Alternative | Reason not chosen |
| --- | --- |
| Bounds on Function Types | Erased values would have to carry bounds in their Type identity and conversions; this stays deferred (SPEC Appendix D) |
| Inferring bounds from bodies without a declaration | It would make a public contract depend on private bodies (Principle 2), unlike the declared bounds of Contracts |
| Checking only at instantiation | Instantiation adds no semantic use conditions (SPEC §8.10), so the requirement must be declared |
| A built-in `ConfinedCallable` Contract | It would duplicate `Callable` for each bound and combination (Principle 1) |
| New vocabulary (for example `pure`) | The vocabulary is closed; adding a bound kind requires its own specification change (SPEC §8.4.10.1) |
