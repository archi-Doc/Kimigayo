# Positional Pattern Binding and match coverage

This increment binds runtime match Patterns, body locals and results, and supplies validated coverage to control-flow analysis. It does not execute matches or certify their ownership.

## Scope and decisions

- Supported Patterns: Wildcard, let/var Binding, Unit, fitted integer/bool/char/string Literal, Tuple and enum Case, with grouping and recursive payload positions. Integer magnitudes use UInt128 and the same target-dependent range checker as expressions; unsigned negative zero is valid. Strings and characters use their decoded values without normalization or runtime construction.
- Case lookup reuses enum identities and qualification. Payload positions use StoredType with the complete subject Type, including Origin substitution; construction hint substitution is not used. Generic T remains structurally opaque, so Literal/Case inspection at that position is invalid. A Binding of T may retain a Deferred acquisition until Copy proof is available.
- Access mode is recorded independently of Type semantics. Binding or ignoring ref/uniq values does not dereference them. Valid structural ref inspection is retained as Pending with Shared/SharedOnce metadata; descendants inherit Shared access, receive no owned acquisition and do not enter body expression Binding. Structural uniq, remaining reference layers after one dereference, object and raw-pointer positions are Invalid. Shared reads, Reborrows and candidate/body Loan checks are future work.
- Pattern bindings and an immediate Block or expression body share one declaration space. An immediate body cannot redeclare a Pattern name; a nested block may shadow it. SPEC §14.8.2 records this decision. Arm scopes are keyed by Pattern roots, so Block/if body scopes cannot collide with them.
- The existing F.5 grammar permits trailing commas in Case payload Patterns. `.Some(let x,)` remains valid and matches one payload; `.Some((let x,))` matches a singleton Tuple payload.
- Guards contribute no coverage. Their Pattern structures are validated, but candidate reading and their bodies remain Unsupported. Control-flow analysis still visits guard expressions, transfers and unsafe operations and records Pending. Candidate and body identities will be separated when guard reading is implemented.

## Representation and shared processing

BoundMatch owns retained preorder position and arm lists. Each BoundPattern carries source syntax, complete MatchedType, parent/element indices, subtree End, Case identity, access mode, implicit dereference, body Symbol, fitted Literal and acquisition. Direct children are walked by advancing to End; there is no per-position child list. These are reusable views of the current Binding pass, not snapshots across rebinding or instantiations. IsCurrent is false when a plan is retired; old table lookups cannot retrieve a replaced match.

Pattern syntax is classified by its arm position, not its parser class. The indexer marks the whole Pattern subtree and registers binding names in the same traversal. BindPattern handles it directly; ordinary BindNode guards against accidental Pattern evaluation. Qualifiers and scaffolding receive semantic completion without becoming calls, getters or enum constructions.

Copy/Move/Deferred is recorded through the existing proof engine after declaration checks. Wildcards acquire nothing. Unsupported shared paths carry access metadata but no completed acquisition or body Type. Existing let/var assignment restrictions recognize initialized Pattern locals.

Match body result types use the same Join as if expressions. Match-targeted yield operands contribute during ordinary Binding, avoiding a second traversal to find results. Implicit Unit is included for allowed nonexhaustive discard Block matches. Control-flow analysis remains responsible for normally completing paths that fail to yield a required result.

## Coverage and diagnostics

MatchCoverage carries Pending, Invalid, NonExhaustive or Exhaustive, a reason and an enum Case declaration index when applicable. Pattern Type/arity validation precedes coverage, and Invalid prevents a cascading nonexhaustive error. Enum coverage tracks absent, partial and whole-payload arms by Case ordinal. Whole-position proofs and boolean root coverage follow §14.8.4 exactly; partial payload/tuple partitions are never combined into a stronger proof.

Containment compares each later Pattern with preceding unguarded Patterns, retaining the first single covering arm. Binding names and mutability do not affect containment; normalized Literal values do. A later guard does not suppress a warning. No warning removes that arm's body from type/result checking.

Warnings have separate retained storage and a final-mode reporting cursor. Reporting twice in one pass emits each warning once; warnings do not change BindingState or CheckBound completeness. Diagnostic publication remains the caller's final-analysis step.

ControlFlowAnalysis uses GetMatchCoverage, removes its independent boolean fallback and never compares a Pattern as an expression. SyntaxControlFlowTypes uses the same limited syntax calculator for its APIs, with grouping transparency and guarded-arm exclusion. Full bound coverage always comes from the Pattern plan.

## Rebinding and performance

Each index pass clears current Pattern/plan keys and rebuilds them. Old Pattern keys absent from the new tree are removed from the scope and Symbol tables. Renaming a Binding child refreshes its Symbol name; unchanged bindings retain identity. Removed plans clear their syntax/position references while pooled containers keep capacity.

The implementation reuses existing Case lookup, complete-Type interning/substitution, capability proofs, integer fitting, result Join and transfer-target resolution. Integer lexical magnitude is read once per Pattern. Position, arm, per-Case coverage, warning and stale-key buffers retain capacity; no per-arm closure or iterator allocation is used in production analysis.

Validation:

- Debug and Release: all 2,394 tests pass, including 88 new Pattern cases.
- Qualified/inferred/root-generic Cases, grouping, nested Tuple/Case structure, exact arity and trailing commas.
- Invalid versus unsupported reference inspection, generic opaque positions, stored Origins and concrete/deferred acquisition.
- 32/64-bit pointer-sized bounds, signed 128-bit minimum, unsigned 128-bit maximum, negative zero and equal escape spellings.
- Coverage reasons, Invalid cascade suppression, single-arm containment, final-only warnings and type errors in warning-covered bodies.
- Arm scope isolation, immediate redeclaration versus nested shadowing, let/var assignment, Pattern replacement/name changes and removed-plan invalidation.
- Guard return/yield targets and syntax unsafe checks, result inference without an expected Type, missing required yields and the ownership boundary.
- Warm Bind plus flow reanalysis: zero allocated bytes at 1, 32 and 128 nested-Pattern matches. Warning-producing warm Bind also remains at zero bytes.
- Release Benchmark build: zero warnings/errors. PatternBindingBenchmark is registered with Bind and BindAndAnalyze workloads. No throughput measurement is claimed.

## Verification boundary

An unguarded valueOrZero match can now finish Binding and result-flow checking. OwnershipAnalysis then reaches its explicit Unsupported Match boundary: UnsupportedCount is positive and IsVerified remains false. It never traverses Pattern syntax as runtime expressions.

The next ownership increment needs whole subject acquisition, selected body-local initialization from payload positions, partial-Move responsibility, per-arm state and departing-scope cleanup. Guards/shared decomposition additionally require distinct candidate identities, shared-reading Types and Loan protection through guard cleanup. Case-specific drop glue, finite storage/layout validation, the generation gate and LLVM/runtime emission remain separate unfinished units.
