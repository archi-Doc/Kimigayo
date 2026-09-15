# Owned match acquisition, decomposition and cleanup

Unguarded matches over supported concrete owned Types now pass ownership verification, including valueOrZero. This extends the Binding and coverage work in C.28 and consumes its retained metadata. It does not add executable lowering.

## Scope and invariants

The supported subset includes primitive/string Subjects and concrete enums whose complete storage passes the existing recursive ownership Type gate. Wildcard, Binding, Literal, Unit, Case and Grouping use bound positional metadata. Recursive enum Case Patterns are supported. Tuple/general aggregate paths, guards, Shared access, implicit dereference, Origin/Loan-bearing storage and generic match acquisitions remain unsupported.

The gate explicitly checks Deferred acquisition, access and dereference metadata before match Place creation. It does not rely on Place rejecting generic parameters: ordinary generic whole-parameter ownership remains available outside this match subset. Rejection tests use T, ref/T and Tuple Subjects that actually complete Binding; guard/shared-structural examples still stop earlier in Binding.

Subject evaluation happens once. Local/parameter acquisition retains its ordinary Copy/Move effect; an already acquired temporary is transferred by InitializeSubject. The source temporary keeps its existing registration and later cleans as Skip. No duplicate registration or Write replacement plan is created for Subject initialization. Wildcards and literal-only matches still acquire their whole Subject.

Never has no Subject value. Non-completing Subject evaluation runs its transfers/calls and creates no arm state. Supporting Block/if handling distinguishes reserved non-value result markers from produced temporaries. This also allows an inner match's Subject evaluation to yield to an already active outer match.

## Verification graph and retained plans

OwnershipMatchPlan retains BoundMatch, Subject/result IDs and a contiguous arm-table range. Each arm records its Pattern root, PatternTest operation and decomposition range. PatternTest describes the entire recursive predicate using the existing normalized Literal and Case metadata. It checks an initialized whole Subject and does not construct literals, invoke getters or acquire payloads.

The analysis graph fans out from MatchDispatch to every arm with the same post-acquisition state. It is the conservative verification graph required by SPEC §14.9.2, not an executable first-match dispatch implementation. Runtime lowering must test Patterns in source order and execute only the first matching arm. A preceding Wildcard or constant Subject never removes later arms from verification. A warning-covered arm that Moves an outer local contributes MayMoved after the join.

Only NonExhaustive coverage creates an Unmatched edge. That permitted discard path cleans the untouched whole Subject and produces Unit. Exhaustive matches cannot reach an artificial unmatched result with an uninitialized output.

## Decomposition and acquisition

A reverse preorder pass propagates the presence of body Bindings to their ancestors. Both Copy and Move need a source Place, so every Case on either binding path decomposes after full Pattern success. Unneeded subtrees remain whole values; the implementation does not expand every possible nested Case or create per-position child arrays.

DecomposeCase shares OwnershipConstructionPlan's parent/Case/contiguous-payload shape. Finalize checks the parent is initialized. Transfer retires the parent's whole responsibility and initializes its direct payloads, the inverse of CompleteConstruction. This represents selected internal Subject storage; it is not a runtime constructor, extra Copy/Move, or permission to partially Move the caller's original enum.

AcquirePattern uses the same state transition as Consume but writes directly into a declared body local. It follows Binding's committed Copy/Move and checks input acquisition consistency. Binding paths never re-prove Copy; unbound residual whole components use ordinary Place classification. Copy leaves the selected source initialized, while Move retires it. Subject-to-binding acquisition remains ordinary Copy/Move even though the Subject itself is compiler-managed storage.

Subject, payload and body-local lifetimes issue Declare before use, including loop iterations. State reuse cannot leak prior MayMoved or assignment history. Each arm clears its payload responsibility before joining; the four existing state lanes remain sufficient without an active-Case lattice across arms.

## Cleanup and transfers

A Subject registration is distinguished from an ordinary Place registration. The existing reverse-sequence merge preserves scope order. When that registration is reached, CleanupSubject follows the currently active decomposition indexes and emits children in reverse payload order, recursively, then the parent. Parent cleanup naturally becomes Skip after decomposition. Child projections are not independently registered as temporaries, so projection-creation order cannot reorder destruction.

This expansion occurs when cleanup is emitted, including inside nested Blocks, return, continue and yield. Nested Subjects retain independent active decomposition indexes. Body locals and Pattern bindings clean before the Subject; Pattern bindings enter left to right and clean in reverse order. Secured result storage survives this cleanup. Match's result is registered as an enclosing-expression temporary only after the result join.

SelectionFrame stores exact target identity, result Place, join and cleanup marks. Yield routes only to a matching active frame. A yield targeting an if remains explicitly unsupported rather than being redirected to a surrounding match. Function return and existing while exit/continue reuse their resolved targets and departing-scope marks.

Abort has no cleanup continuation. Unselected arms acquire no runtime responsibility. Intermediate temporaries created while evaluating the Subject retain their enclosing expression lifetime; the match cleanup mark is taken after Subject expression evaluation so it does not end those lifetimes early.

## Reuse and integration

OwnershipBody.Reset clears MatchStorage, MatchArmStorage and DecompositionStorage, retaining capacity. Bind already invalidates ownership certificates. Changed enum storage is checked again and cannot reuse a positive support result or stale match plans.

The implementation uses value records and integer indexes, shares construction-plan representation and solver transitions, and avoids per-arm closures and per-position child collections. The active-decomposition lookup is extended only for match operations; ordinary whole-Place bodies incur no per-Place lookup construction.

The Parser now skips dedent separators only when they lead to the required next argument comma. This supports a block-valued match followed by another call argument, without treating a dedent as a comma. LF, CRLF and CR regression cases exercise the path; missing-comma input remains invalid. No SPEC change was needed.

## Validation

- 59 new tests; 2,453 total tests pass in Debug and Release.
- Subject Copy/Move, evaluation once, temporary materialization, literal-only string consumption and primitive/string/enum patterns.
- Copy-only and recursive decomposition, binding order, residual reverse-structure cleanup, varying selected Cases and enum-valued results.
- Full Pattern tests before acquisition, every arm's intact input, no unmatched exhaustive edge, and MayMoved from warning-covered arms.
- Result-before-cleanup, deep return with an outer call temporary, nested Subjects, while continue, unmatched Unit and no cleanup after Abort.
- Never call, parenthesized Block and if Subjects; nested Subject evaluation yielding to an outer match; refusal to redirect if-targeted yield.
- Loop Declare/state reset, changed-storage invalidation and actual ownership-layer refusal of generic/reference/Tuple matches.
- Warm analysis-only and Bind plus both analyses allocate zero bytes for 1, 32 and 128 nested-enum matches using the existing strict measurement helper.
- MatchOwnershipBenchmark is registered with Analyze and BindAndAnalyze workloads. Release Benchmark build has zero warnings and errors. No throughput improvement is claimed.

The existing WarmBindingAndBothAnalysesReuseStorage test intermittently reported 7,856 bytes in Debug full runs (4 of 150). The bytes were not allocated by the compiler. Allocation-free checkpoints showed the jump landing in Bind, in Ownership.Analyze or between them. No JIT ran on the measuring thread and no new collection occurred, while total GC pause time grew by 2–3 ms. The amount was the unused tail of the measuring thread's allocation context: its first warmup iteration used 344 of 8,200 bytes. Allocating a 1,024-byte array before measurement changed the result to exactly 7,176 bytes and made other zero-allocation tests fail with the same value.

AllocationMeasurement now forces a blocking collection after warmup, so the thread enters the measured interval without a partially used context. Allocating work still acquires a new context and is reported. In 100 interleaved full Debug runs per variant, the unchanged helper failed 3 times; the collection variant failed 0 times, including when the 1,024-byte array was allocated first. The measured operations, warmup, four iterations, zero-byte requirement and phase recording are unchanged. No compiler change or runtime configuration override was needed.

Guard candidate identities/shared-read Types, Loan protection, borrowed decomposition, generic match effects, Tuple/general Partial Move, type-level drop glue, finite storage/layout validation and executable emission remain outside this increment.
