# Binding review — 2026-10-03

A review of `src/Kimi/Compiler/Binding` (≈37,000 lines, one `partial class Binding`) for structure, defects, maintainability and measured performance. Five read-only reviewers covered the lifecycle and publication core, expressions and calls, Types and Contracts, Origins and storage, and collections, patterns and the Kimi library catalog; every defect they reported was reproduced through the CLI against the pre-change compiler (`091cc7a9`) before it was accepted. The SPEC is the authority; tests and the current implementation were not. Reproducers below are the minimal programs of the review; the commits hold the regression tests.

## Structure

- **Pass phases.** `Binding.Bind` is `ResetPass`, `IndexSources`, `IndexLibrary`, `BindDeclarations`, `BindBodies`, `ValidateBoundDeclarations`, then `Check`. `Invalidate` uses the same `ResetPass`, so an edit and the next Bind discard the same per-pass facts (`9e82d571`).
- **Failure facts.** A node keeps its first failure only. `FailExplained` records the facts that explain a failure (write target, mismatch, operator operand, acquisition Place, capture entry) only with that failure, so every fact table answers for its node's one failure; `ReportIssue` publishes one direct failure (`040919f3`).
- **One structural completion.** Result joins, body checks and conversion probes share `ResultStructure()`, created with one Never evidence (`2f48a2cf`).
- **Requirement reference.** `BindCall` saves and restores the enclosing call's bound Contract reference (`be7286b7`).
- **Form failures.** A group shape failure or a misplaced protected form (`IsFormFailure`) is an error of the form, not an invalid context for the declaration's members and uses.

## Repaired defects

| Commit | Defect (SPEC) | Reproducer |
| --- | --- | --- |
| `7d14d940` | CheckFaulted_Kd: a misplaced positional `match` argument bound its arm Patterns alone; a generic application on an exclusive or consuming closure read an unbound callee (NullReferenceException) | `f(a: 1, match c ...)`, `next<i32>(1)` |
| `5a869aba` | `.length`, `capacity`, `isEmpty`, `indices` selected through `raw/Array<T>` and `obj/Array<T>` in safe code (§3.4.1, §12.4.1) | `func f(p: raw/Array<i32>) => p.length` |
| `2f48a2cf` | Acceptance and the inferred Type depended on declaration order: whichever check created the shared structural completion fixed its Never evidence | `let x = if c => 1@i64 else => stop()@i32` after `func helper() -> i32 => 1` |
| `be7286b7` | `check` passed and `build` failed with GenerationFailed_Kd: a later argument's synthesized call cleared the requirement reference | `items.index(0..2)` with `S is Indexable<Range<i32, i32>>` |
| `2a346622` | A read bound `indexUniq` of a refuted conditional `UniqIndexable` and published NoApplicableOverload_Kd (§4.6.9, §8.4.8.2); the update now explains the missing conformance | `Self is UniqIndexable<isize> when T is Copy`, `names[0]` on `var Box<string>` |
| `ba5e3df9` | An inherited `Self is ObjectPayload` did not prove `objref/Self` in a refining Contract (§8.4.7.2) | `contract C: P` with `P`'s clause |
| `cd4a39f5` | A Never left operand expected Never of the other operand (§3.1.5, §13.4) | `$abort("x") + 1`, `$abort("x") == 1` |
| `e24e1a68` | `protected` accepted at the root, on enum members and local functions, and reported as InaccessibleBinding_Kd in groups (§9.3); new ProtectedPlacement_Kd | `protected func f()` |
| `3243730c` | A qualified Name selected the Type's generic parameter, and a derived nested `T` collided with the base's parameter (§9.1) | `let v: Box<i32>.T = 1` |
| `d0cbc1a8` | A tautological Origin clause changed a pair result's omitted Origin (§15.4.1) | `origin static outlives static` in `func g<s/T>(x: s/i32) -> s/i32` |
| `9e82d571` | Per-pass state: stale `partPrerequisites`, a previous Result over a failed pass, duplicate derived records from `CheckBound`, per-pass tables kept by `Invalidate`, unpruned lists on the invalid-library path | — |
| `040919f3` | A duplicate parameter was published at an unrelated rejected capture entry | `func [n@uniq] (a: i32, a: i32)` |
| `4ff9b370` | Uses of a failed declaration repeated it as direct InvalidConstraint_Kd records at calls and members (§23.3.6.4) | two equal `func add`, then `add(1)`; a field of a struct with `#Layout("c")` |

Each unit was verified with `scripts/verify.ps1` (evidence under `artifacts/verify/2026100*-unit-binding-review-*`); the diagnostic snapshot equals the pre-change baseline (`20261002-230139-213-unit-binding-review-baseline`) apart from two added syntax corpus cases. Session `20261003-004735-250-session-binding-review-session` (source `4ff9b370`): warning-free whole-solution Release build, 15,417 functional and allocation tests, 10 native O0/O2 executions and 33 milestone harnesses, 0 failures.

## Performance

Measured with an interleaved, pinned A/B harness with full JIT (`artifacts/benchmarks/20261003-binding-review`): reusing the Kimi library's conformances and constraint environments proven before the bodies (`709faaba`) lowers a warm Bind from 9.24 to 7.91 ms (Milestone1) and 10.13 to 8.81 ms (Milestone41), about −14%, and the cold first Bind about −12%. Warm rebinding of every complete milestone program allocates 0 bytes (`5cb1aa13`: a reusable closure classifier, no boxed capture enumerator, scratch specialization binders). A per-walk memo of constraint uses measured within noise and was dropped. The remaining fixed cost is mostly the library: its bodies are bound again every pass (about 2.4 ms) and its constraints re-proven.

## Open findings

Reproduced, not repaired in this review; each needs its own unit or a decision.

- **Valid programs rejected.** A Contract refining two bound references of one Contract (`contract Q: P<i32>, P<i64>`) keeps only the first requirement identity. Arguments never get the expectation of a single remaining outer candidate (§10.5). A specialization cannot inherit a result-only Origin (§8.8.2). Static storage elision (§15.4.2) is unimplemented and its limit is reported as a language error.
- **Cascades still published.** An invalid constraint environment (contract cycles, contradictory premises, a missing constraint Name) fails every member and use; a failed attribute also fails its target with InvalidTypeFormation_Kd; invalid bases repeat at each derived struct; one unproven constraint through a pending match plan gives six records; an invalid pattern is reported up to three times; an Origin result completed to `static` is reported up to eight times.
- **Wrong or missing facts.** Bare TypeMismatch_Kd without Types at about 20 sites (unary on unsigned, if-branch mismatches, fixed-array literal counts, for-loop shapes, Tuple selectors); an uninferable Type argument reported as UnprovenConstraint_Kd; a private method call reported as UnresolvedBinding_Kd; `(f)(1)` unresolved (repaired 2026-10-05: a parenthesized function name is called by name, `ParenthesizedCalleeTest`); a misplaced `$expect` reported as InvalidTestDefinition_Kd; a duplicate parameter spanning the signature without related location (parameter symbols declare the function as their declaration).
- **Edit-time state.** Node-keyed caches grow without pruning. The product checks a fresh compilation each time, so these affect tests and future Mods only. Same-sized Origin/generic schema edits are repaired by the follow-up below.

Repaired after the review, in P31 session 1 (2026-10-03): range literal keys (`34483f6c`, `6009ba16`), synthesized calls that followed an edited key (`42571e71`), the repeated loop binding (`8ae903a7`), fill literals resting on a failed part (`34450d86`), closure Types in diagnostics (`731ba7e3`) and startup records at the signature (`7f74b658`).

Repaired 2026-10-06 (`CallAdaptationTest`): fixed destinations and candidate result filtering share argument adaptation (§10.2–10.3), including temporary borrowing without lifetime extension. Acquisition repairs are retained only when the named candidate's other inputs, result and Constraints apply; mismatched Types no longer suggest `@move` or `@uniq`.

Schema-edit follow-up (2026-10-08): `BindSchemas` compares the complete ordered header and inherited slots before reuse, including Origin names and locations and generic symbol/kind/pair correspondence. Renaming an interned Origin replaces the current atom without changing the old expression. `OriginSchemaEditTest` covers signature and nested-container edits, unchanged rebind identity and zero warm allocation. Node-cache pruning remains separate.
