# Appendix A. Compiler implementation requirements

[Specification index](../../SPEC.md)

**Normative.** Compiler requirements preserve the language’s information and invariants; they add no source syntax or failure behavior. Appendix B gives optional algorithms. Verification coverage below is required; parsing alone does not establish it.

| Term | Meaning |
| --- | --- |
| Koto / Koto tree | Parsed syntax nodes with source contexts and parent/child relationships; not a bound program or binary interface. |
| CodeContext | Source-local lookup and diagnostic context for one immutable source snapshot. |
| Directive Binding | Resolving and validating compile-time Condition names and dependencies. |
| Validation obligation | Information retained until a required check can be completed. |
| Finalization | The point when a declaration, layout, specialization, or executable body is accepted for subsequent compilation; required checks must be resolved. |
| Lowering | Translating checked source operations into lower-level representations while preserving semantics. |

## A.1. Source identity and incremental analysis

A CodeContext belongs to one Kotonoha and immutable SourceDocument snapshot (§18). A new snapshot requires a fresh source context and current validation of aliases/Binding even at the same path. Verified semantic records may be reused only after the correspondence, dependency, and source-mapping checks in §18.7; this does not reuse stale Koto/CodeContext objects. Source-less contexts cannot replace parsed nodes' source identity, and nodes cannot move into another Kotonoha's Container.

Retain each node/fragment’s original CodeContext and diagnostic/header locations after merging. Resolve its bodies, headers, Types, and Constraints in that context, not the merged Container’s. Generated documents have their own contexts; source-less roots/wrappers supply no alias environment and must preserve wrapped syntax’s original context.

Lowering places a selected top-level runtime body in a private internal function, preserving its SourceDocument scope and CodeContext. It does not synthesize a public main or permit top-level return (§22.2).

The internal name `MacroKoto` does not define language semantics; `$` is the Composition Root.

A Kotonoha tokenizes and parses each `SourceDocument`, merging declarations into one root Koto tree. Root executable syntax is placed as described under [root and nested containers](../06-declarations-and-containers.md#611-root-and-nested-containers).

## A.2. Immediate directive validation and selection

Verify case-sensitive prepared-environment lookup and collisions under §2.5, including differently cased settings and rejection of invalid/non-NFC configured names. Selected `#if`/`#case` items share the surrounding scope: verify later name visibility, duplicate declarations, defer registration, cleanup timing, and transfer targets without an extra directive scope.

The Parser validates and evaluates each reached Condition against the prepared Compilation environment during parsing. The result is True, False, or Error; there is no internal Pending state or later Directive Binding. Unknown Names are diagnosed at their original source locations immediately.

A validated True #if contributes its Target directly; False contributes none. An invalid #if reports its Condition errors and skips its target for recovery. No #if wrapper or pending-condition storage is needed.

A reached #switch validates every explicit arm Condition and parses every arm under §19.3/§19.5, including nested Conditions in unselected arms except inside False #if targets. After successful validation, it contributes the first matching Block directly. Invalid Case Groups may retain this representation for error recovery:

```text
CompileTimeSwitchKoto
    CompileTimeCaseArmKoto[]
        Condition or fallback
        Block
```

Condition validation never invokes ordinary Binding of excluded syntax. Once reported, Condition diagnostics preserve their original SourceDocument and span even when the Condition node is discarded. Instantiation and implementation selection never reselect directives or mutate shared Koto. Eager, deferred, and cached source parsing must agree on acceptance and mandatory diagnostic categories under §19.5.

## A.3. Binding, caches, and incremental validity

Mod execution and provisional Binding follow [§20.7](../20-compilation-configuration.md#207-mods-source-generation). After generation, final Binding preserves the following stages; Mod updates may repeat affected analysis but never reopen a finalized program:

```text
Complete source parsing and Mod output integration
-> select directives in established environments
-> collect selected declarations, root and scope tables, and alias targets
-> bind headers, Types, Signatures, and Constraints; validate merges/duplicates
-> match explicit specialization headers; close their set before final call targets
-> validate inherited Names; retain candidate conformance/receiver mappings
-> resolve bodies -> test candidates -> select operations; retain pending usage obligations
-> compute effect-family fixed points; complete public guarantees and conformance proofs
-> discharge usage obligations without lookup/overload reselection
-> instantiate static arguments and select the required implementation
-> retain Symbol references and the selected operation plan for lowering
```

Separate Type/Value and Origin/Label tables. Lookup Context retains source, scope, namespace, role, and access; outcomes retain Symbols, function groups, deferred work, or errors. Lowering never resolves strings again. Share normalized Types, but isolate candidate variables, constraints, mappings, tentative bindings, adaptation plans, and rejection reasons; commit only the winner. Early filters must preserve lookup stopping. Pairwise ranking may take O(n²) comparisons, excluding Type-comparison cost.

Pending conformance mappings are not proof evidence. Resolve information needed for lookup before computing effects; an empty or missing summary cannot fill unresolved work. Complete implementation proofs, public statuses, conformance, and use obligations in dependency order. Effect closure cannot justify a cyclic conformance, and no unresolved ObjectCompatible status may be published. Preserve ordinary errors separately from a valid implementation's NotProven status.

Cache only context-independent results or include every relevant dependency:

| Cache | Required distinctions |
| --- | --- |
| Lookup | Scope, name, namespace, role, lexical visibility, source alias environment |
| Accessible lookup | Also use-site Kotonoha, Container relationship, and inheritance/access domains |
| Member lookup | Target Symbol/Type, type arguments, static/instance use, protected receiver Type; no extension candidates in this revision |
| Applicability | Candidate, argument Effective Types/literal values/labels/forms, expected Type, type arguments and constraints |
| Environment selection | Compilation target, configured Project settings, selection state; independent of generic arguments |
| Conditional conformance | Generic declaration, substituted arguments, D/P proof environment, evidence paths, and verified implementation/associated-Type mappings |
| Receiver guarantees | Call operation Identity, complete implementation-family contents, root summaries, and verification dependencies; concrete caller arguments do not create a new public status |

Do not reuse results across different roles or unrelated access Containers. Source, dependency, alias, selection, or Symbol changes invalidate affected caches. Loans/initialization affect Usage Legality, not overload-cache keys; refinement affects input Effective Types and therefore member/applicability keys.

Apply §21.3.4 to all declaration-dependent judgments, including absence dependencies. Test Name/specialization additions, access expansion, Signature changes, and Proven withdrawal against stale artifacts. Reject consumers that lose a requirement, accept compatible changes after revalidation, and diagnose missing rebuild information without depending on load order.

Verify source isolation, merged private access, duplicates/roles/arities, Type/Value paths, aliases, unavailable extensions/modifiers, expected results, incomparable candidates, nested inference, generic environments, and no usage-failure fallback. For §2.5.1, cover declaration, requirement, constructor, deinit, and accessor prefixes while retaining ordinary same-spelled Names and independent next-line declarations. Load, parse, generator-completion, and candidate order must preserve results.

After merging/inference, check recursive API access domains (§9.3), retaining dependent associated-Type obligations. Cover private Types in internal APIs, restricted enclosing Containers, private generic arguments/helpers across Kotonoha, sealed bases, open-fragment agreement, base access, protected receivers, compound access, and §6.2.2's inherited-Name prohibition. Include all ancestor layers, Property access independent of accessor access, conditional members, and fragment/Mod additions. Access caches depend on base graphs/modifiers and completed Container contents as well as aliases.

Retain parameter immutability, receiver index/kind, argument mappings, and default environments (§7). Verify default Loans, partial-call cleanup, and receiver-first evaluation independent of parameter position. Inherited lookup caches preserve the committed layer; applicability/accessor failure cannot add base overloads.

Persist §15.6.4’s static effects and returned Loan anchors in artifacts/callable data. Use stable Field Identities, recursive summaries, and initializer/destructor dependencies; summary changes invalidate callers. Missing/incompatible summaries require conservative effects or diagnostics. Test Loans across direct, recursive, indirect, and separate-module mutation, default/cleanup effects, and permitted shared reads. Runtime Origin erasure must preserve these proof dependencies.

## A.4. Property implementation requirements

Represent let/var storage, standard operations, custom/computed accessor signatures, and Contract requirements explicitly. Preserve storage/base identities, complete Types/Origins, accessor access, function boundaries, and witness mappings. Standard get is not a synthesized source function; Copy does not change its result Type.

Validate direct/child Place permissions, implicit receiver adaptations, generic Copy proof and conditional Move states, first-placement history, construction-call bans, getter temporary restrictions, and normal cleanup before lowering. Preserve Contract result restrictions through witness optimization. Update parser, writer, grammar, serialization, diagnostics, and artifact invalidation for accessor syntax and removal of explicit Move.

Cover private-set Move rejection; custom-get result versus storage borrowing; non-Copy custom setters and self-assignment; construction branch joins; partial inherited access and ancestor deinit; reference-result Origins; static accessor effects; and Contract by-value versus shared-slot witnesses.

## A.5. Raw pointer backend requirements

`ptr` is not a Primitive Type. LLVM `ptr` is a backend representation; instructions supply the Types needed for memory access and arithmetic. Lowering must preserve this specification and use properties such as `inbounds` only when their premises hold. Language undefined behavior and LLVM poison are distinct concepts. The initial instruction mapping is specified in §21.5.3; pointer/integer round trips add no provenance guarantee.

## A.6. Literal representation

Preserve integer magnitudes and exact decimals until fitting. Canonical serialization must preserve literal kind and fitted value for every allowed target; never round decimal literals through f64. Serialize strings with their values after newline normalization and escape/interpolation processing. LLVM emission and sharing follow §21.5.6.

## A.7. Cleanup analysis and lowering

Analyze defer registration separately from execution: non-completing cleanup affects exit paths, not the path after registration. Lower defer and destruction into one exit sequence, retaining only needed registration state. No dynamic closure, function value, or heap cleanup stack is required. Initial physical lowering and slot responsibility follow §21.4.

In `if condition` containing only `defer => cleanup()`, the true branch registers and runs cleanup before its own exit; false does neither. This needs no registration flag, and lowering cannot move cleanup outside the branch. cleanup is an illustrative API.

Retain constructor choice, initializer environments, per-layer completion, component initialization/responsibility, and the Destruction receiver’s exact layer. Cleanup places the base after own fields; a completed base keeps its destructor if derived construction fails. Interfaces retain constructor access/signatures, destructor presence, logical component order, and exact dynamic cleanup chains. Physical offsets or apparently empty destructor bodies cannot replace these facts.

Verify fragment/selection deinit duplicates; forbidden placement, modifiers, and calls; implicit-constructor suppression; base-constructor access; completeness before/after constructor cleanup; partial Tuple/array construction; reverse element/base order; no destruction accessors; and skipped Moved components. Also cover partial-value replacement, interrupted cleanup, receiver escape, base slicing/replacement rejection, and one final-reference object cleanup.

## A.8. Callable and object verification

Verify OwnedOrigins through raw pointees, unused Type/Origin slots, captures, and recursive payloads; test `a : static`, unresolved abstract Origins, Function Item bound arguments, and fixed callable signature Origins versus per-call binders. Reject mutable-static borrows at Owned boundaries, including after capture and across modules. Checked casts may supply only proof-covered fixed Origin bindings as static; preserve outer handle Origins and never bind per-call callable Origins. None of these checks may depend on a private body being available to a client.

Verify input-derived Callable results for all three receivers: per-call Origins, multiple-input meets, retained result Loans, rejection of a shared-to-exclusive upgrade, and rejection of hidden-receiver or call-local result dependencies. Common Function Type compatibility uses the same argument/result relation while retaining its separate Owned-environment restriction.

Retain capture bindings, acquisition order/effects, mutability, nested dependencies, environment identities, and capture/call/result Origins and Loans through compilation and artifacts. Resolve required Copy, receiver, Callable, and erasure obligations before finalization. Check concrete Copy independently of receiver kind and erased-container classification.

Verify §12.4.4's single public status per call operation, standard Place/witness distinction, and common implementation-family guarantee. Cover Whole/Base replacement, incomplete MoveOut, exclusive escape, permitted Part replacement/exchange, reference-field versus referent aliases, captures/statics, defaults/cleanup, returned borrows, and separate/indirect/generic calls. Verify recursive fixed points without treating circular conformance as evidence. A NotProven specialization changes the public guarantee without a declaration error for that reason; its cause must be traceable at a failing use. Unknown, missing required data, and unfinished verification cannot supply proof.

Verify multi-level generic base projection, original protected-receiver checks, shared/exclusive access, returned-Loan anchors, direct Field access, and rejection of whole-base replacement, consuming receivers, and unbound derived/base argument conversion. Preserve these results across separate compilation and optimization.

Verify object creation's Copy/Move input states, payload eligibility, fresh identity, initial strong count, allocation failure, and source-storage responsibility. Strong-owner duplication must preserve identity without retaining a Loan on source-handle storage; cover count overflow, release through different views, exactly one final cleanup, and resurrection rejection.

Test capture versus call acquisition, unused/nested explicit captures, let/var, reference/referent lifetimes, Copy/Move-consuming calls, every Callable receiver, per-call Origins, variant cast Loans, static inherited calls, and construction/destruction restrictions. Verify public status, use legality, complete dynamic cleanup, and receiver adjustment independently of load order and optimization.

## A.9. Refinement and require verification

Retain require’s condition, failure body/form, and layout as a statement; resolve transfers before any Selection-style lowering. Distinguish Requirement Tests from runtime is by syntax context. Prove failure non-continuation independently of condition truth, including caught transfers and actual cleanup paths.

Retain Binding Identity, Value Instance, Effective Type, and validity conditions; coordinate refinement with initialization, Loans, transfers, cleanup, and reachability. Cache Effective Types. Facts cannot cross old aliases, Boolean variables, later defer-registration states, or Function Boundaries. Short-circuit/loop joins must be analysis-order independent.

Cover same/next-line `else`, comments, multiline conditions, missing or empty bodies, forbidden expression placement, both `is` contexts, single evaluation, trivial tests, invalid targets, early transfers, contradictory facts, Move/Replace invalidation, surviving member mutation, alias versus new-binding typing, failure validation even for `require true`, the retained static true successor of `require false`, and unchanged acceptance under optimization. Type-checking continuations must preserve valid refinements without restoring Moved values/ended Loans or adding execution edges; include their result constraints without merging their state into live paths.

## A.10. Enum and Pattern verification

Preserve Case Symbols, Pattern positions, guard/body Binding Identities, read/binding Types, Access Modes, acquisition effects, and Origins/Loans through compilation. The [reference plan](B-reference-models.md#b6-match-binding-plan) shows possible storage. Resolve Cases, Copy obligations, and dependent reads by their deadlines. Type alone does not identify owned versus shared access.

Verification must cover at least these boundaries; neither parsing nor optimization establishes the guarantees:

| Boundary | Required result |
| --- | --- |
| `.Some(0)` and `.None` alone; all arms guarded | Missing coverage in any match |
| `Option<bool>`, Tuples, nested enums, multiple earlier arms | Require whole-payload/whole-position arms or catch-alls; partial partitions do not combine into coverage, and unions do not trigger mandatory unreachable warnings |
| Same-Case/Tuple containment, duplicate Literals, guarded earlier/later arms | Mandatory warning only for the specified structural containment by one earlier unguarded Pattern; a later guard does not suppress it |
| Duplicate names, wrong Case/arity/Type, `.Some` or `.None()` construction | Errors regardless of reachability |
| `i8` literals `-128`, `128`, `-129`; `i32` arms `0`, `-0`, `0x00` | Fit after sign; reject the two out-of-range values; warn on the two later equal unguarded values |
| Origin-bearing ref/uniq payload construction, acquisition, cleanup | Preserve dependencies/capabilities; never destroy borrowed referents |
| Named/single-Origin mapping and `View<T>.Some` | Equivalent mapping; reject the enum's own Origin annotation in the Case qualifier |
| Unconditional Copy with unconstrained T / constrained T / only ref/T payloads | Declaration error / valid derivation / no referent Copy premise |
| Conditional Copy when T is Copy | Type remains usable for non-Copy T; Copy is available only when its condition is Proven |
| Core Option/Result conditional Copy | Check both Result payloads, nesting and complete Semantics; active Case does not change capability. Verify reuse versus Move, Unknown versus Refuted, and borrow dependencies after Copy |
| Core condition-atom sets | Accept both orders, grouping, and duplicate atoms; reject missing/unconditional Copy, missing/extra atoms, and wrong Symbol Identity. Check generated and loaded definitions without spelling-based user-enum behavior |
| Shared reads of Copy Result payloads | Slice and ref-Pattern reads produce values; explicit element @ref retains storage/Origin and tryGet keeps its reference result. Generic head with plain s[0] fails definition checking; no borrow-binding selector is introduced |
| Child structural Patterns, Grouping, `ref/ref/T`, `ref/uniq/T`, `uniq/T` payloads | One dereference per position; retain shared access; reject direct structural matching of remaining reference layers; allow binding then inner shared match |
| Same Non-Copy payload Type on owned/shared paths | Body acquisition is Move/shared reading respectively; test string, object, and exclusive-reference payloads |
| Non-Copy subject with only `_`; false guard followed by acquisition | Initial whole Move even without bindings; preserve payload for the next arm without double destruction |
| Same name in guard and body | Separate Identities and scope-specific Types/overload resolution; no automatic refinement transfer |
| Guard Move, exclusive borrow, mutation through aliases, or direct candidate capture | Errors, including direct Copy candidate capture |
| Saved Copy values, stored references, and Copy aggregates | Check transitive Origins/Loans and destination; ordinary argument passing does not itself prohibit storage |
| New candidate-dependent Borrow/Reborrow escaped directly or through a callee | Error; dependent Loans end inside the guard |
| Guard outward `return`; Abort/divergence or non-normal guard cleanup | No body initialization or later arm; ordinary transfer secures its result then cleans guard/Subject, while Abort does not unwind |
| Match return/yield/outward transfer | Result acquisition, arm cleanup, then remaining Subject cleanup; no destruction of borrowed referents or unmatched completion |
| Borrow into owned Subject versus returning a stored external reference | Reject Subject escape; permit external reference only under its existing contract |

## A.11. Static Contract verification

Keep Contract/Requirement identities, parent edges, associated-Type declarations/projections/specifications, requirement signatures/Constraints, and conformance mappings separate from ordinary members. Collect Constraints before resolving projections; normalize explicit associated identities before matching, never infer them from implementations. Origin-free matching keys must retain Origin contracts elsewhere.

Verify requirement access/parameters, parent cycles/conflicts, duplicates before matching, selection before compatibility, conformance access domains, and explicit/implied parent mapping consistency. Cache projections/candidates with their conformance/Constraint environment. Shared-candidate proofs must cover every substitution independently of optimizer code sharing.

Test qualified/ambiguous associated Names, diamonds/independent declarations, inherited/missing Type specifications, input/Origin matching, getter covariance/setter structure, redundant/conflicting parent mappings, and static calls/unsupported runtime Views. Cover safe/unsafe functions, defaults, nonstrengthening Constraints/Effects, and public conformance with private implementations.

Conditional conformance checks also cover positive-only syntax, permitted block declarations, Type-use versus conformance premises, condition-scoped signatures, duplicate direct registrations across fragments, cyclic/Unknown evidence, and parent-path agreement. Compare applicability only after committed lookup and substitution; test same-group alternatives, no base fallback, no condition-strength ranking, and no instantiation-time reselection. Verify conditional Copy derivation and Field bridges under their declared premises. Artifacts/cache invalidation preserve conditions, mappings, associated-Type bindings, and proof dependencies independently of environment directives.

Test struct-conformance inheritance under §8.4.4: a compatible Stringify-like mapping succeeds; Self arguments/results and owning receivers fail that path without invalidating the derived declaration. Cover fixed associated-Type normalization, alternative paths, intrinsic exclusions, and Unknown/Error distinctions. Across A -> B -> D, retain A's Member Identity/Self and composed Type/Origin/receiver mapping. Diagnose explicit conformance and constraint-use failures with the failed requirement/cause, without warning on the open base. Generic calls use the retained mapping, not caller-side member lookup.

## A.12. Generic schemas and specialization verification

Preserve complete Type slots rather than flattening a pair into two independent arguments. Keep function length slots distinct from Type slots. A logical schema contains:

```text
DeclarationSchema
    GenericSlots: ordered index, kind (ordinary Type, pair Type, or function length), bound names
    OriginSchema: ordered binders, bounds, inferred variance and Loan requirements
PairSlot
    WholeType: complete or dependent Type
    OuterSemantics, DirectTarget, OuterOrigin: projections of WholeType
LengthSlot
    Value: nonnegative isize constant or bound dependent length expression
DeferredObligation
    requirement, source use, defining bindings/environment, dependencies, deadline
```

Use Origin schemas for argument correspondence, fragment-header matching, and artifact compatibility, not overload identity. Preserve Binding Identities through alias expansion and Signature normalization. Prove role restrictions before using a projection, or retain only a legitimate obligation. Semantic metadata must not discard Origin information merely because runtime descriptors omit it.

Verify declared Origin bounds at both definition and use: forward binder references, unknown/duplicate binders, equality cycles, `static`, substituted call/Type bounds, fragment agreement, nonstrengthening Contract implementations, and inherited specialization bounds. Bounds never supply a Loan or add overload candidates. Distinguish a local borrow's carried Origin from its slot's inferred Origin; reject bare owned-local names as Origins and preserve independent nested dependencies.

Fixed-array verification must cover contextual of/length, explicit slot kinds and conflicts, parenthesized constant expressions, constant-readable eligibility, expected-Type boundaries, candidate-local literals, element counts, zero length, zero-sized elements, recursive/nested layout and stride, normalized dependent expressions, negative intermediates, public ValidLength failures, universal body checking, specialization keys, and artifact invalidation after constant changes. Check whole initial construction, static-path reinitialization after Partial Move, reverse partial cleanup, and literal-only Move Paths.

Verify full-Type binding, one-slot pair decomposition, reconstruction versus applying s elsewhere, duplicate names, trailing commas, occurs-checks, invalid arguments, recursive storage, and input-order-independent principal Origins. Origin simplification must preserve distinct Loans.

Test specialization ambiguity before contract checking; inherited defaults/Safety; closed arguments; Origin-only duplicate keys; obj/D versus rc/D; mandatory selection through shared callers/references; recursion; and no fallback. Verify unused selected declarations, closed-set ownership, and set/body invalidation. Code sharing, budgets, automatic specialization, and LTO must preserve behavior. Cover unconstrained reuse/capture rejection, fixed Property results, shared element-read families, and conditional Copy/Move plans, caller-independent definition checking, and no hidden applicability conditions. Instantiation preserves §8.10’s symbolic ownership/cleanup plans.

## A.13. Sequence access verification

Bind index versus range access by argument Type, not literal syntax. Retain receiver/element Places, access form, bounds, permissions, acquisition, complete result Type, and Origins/Loans. Nested access cannot read/copy intermediate arrays. Preserve generic SharedReadResult correlations and validate every admitted case before lowering.

Preserve one-time receiver/argument evaluation, index-evaluation protection, exclusive activation after bounds checks, RHS-first assignment, and target-first compound assignment. Apply common increment/exchange rules. Metadata snapshots add no source Loans and waive no receiver validity checks.

| Verification area | Required coverage |
| --- | --- |
| Index and Range | Lengths 0/1, ^0/^1, negative offsets, excessive distances, saved values, half-open/inclusive/reversed ranges, operand evaluation before construction checks |
| ResolvedRange | Maximum-isize end, finite and permanently exhausted iteration, rejection of direct Range iteration, reuse against shorter/resized targets |
| Place acquisition | Copy versus ordinary/explicit Move, literal-only eligibility, runtime/Index shared reading, nested writes without intermediate Copy, reinitialization after Partial Move |
| Slice boundaries | Zero-based reslicing, split endpoints, None from each try operation, failure inside arguments remaining Abort |
| Lifetime and storage | Borrowed Array/Slice retention, temporary/local escape, copied references versus slot borrows, nested Origins, whole-array Loans including empty/split views, rejection of mutable-static borrows at Owned boundaries |
| Metadata and iteration | Receiver effects, completeness checks, no result Loan for metadata, stable saved indices, reference iteration independent of element Copy, no iterator-owned borrowed results |
| Lowering | Identical acceptance, results, effect/Abort order, and Loan legality with optimization enabled/disabled; O(1) view operations without element-proportional allocation or Copy |
| Dynamic mutations | §4.7's success/absence/duplicate outcomes, ^0/^1 and maximum-length cases, RHS-first Dictionary replacement versus argument-first insertion, stored-key identity and remove/re-add order |
| Capacity and cost | No internal allocation within capacity, including deletion churn; reserve additional arithmetic and overshoot; shrink failure preserves placement; growth/reindex operation counts meet amortized bounds; cleanup-free Array clear is O(1) |
| Dependencies and effects | No dependency subtraction after clear/None/Err, old-result versus new-input dependencies, uniq element conflicts, receiver-before-argument Loans, generic equality/destructor summaries and static reentry |
| Temporary argument borrowing | Typed/generic/unfitted inputs, candidate ambiguity, normal temporary expiry and rejected escape, no implicit extra reference/handle layer or exclusive-temporary extension |

## A.14. Layout, LLVM, and runtime verification

Validate windows-x64-v1 with LLVM 22.1.8, distinguishing semantic acceptance, TypeLayout, IR structure, object ABI/dependencies/unwind, and actual execution. The verifier alone cannot establish layout, foreign ABI, ownership transfer, or correct startup. Keep input/target/settings/expected results together; retain representative golden IR plus structural checks, and normalize only irrelevant internal numbering/paths.

Internal-function IR/signature expectations test the selected compiler implementation, not a stable language ABI. When its choices change, update those expectations and verify matching definitions/calls/adapters, artifact invalidation, source-order acquisition, ownership/result delivery, and unchanged external contracts. No cross-build internal ABI compatibility test is required.

| Area | Required coverage |
| --- | --- |
| Startup | Unique implicit/explicit body; uninitialized top-level let/var and Unit; empty/declaration-only documents; invalid/duplicate main; mixed forms; selected/generated items; Library restrictions; static-only documents |
| Layout | Attribute errors and fragment conflicts; single C storage fragment; moving the entire fragment/renaming method fragments; nested/zero-sized/aligned Types; generic substitution; inline cycles; overflow; C size/alignment/offsetof under packing 16 |
| Aggregate order | Alignment sorting with logical-order ties across own Fields, captures, Tuples and Case payloads; fixed base/tag/array positions; whole-instantiation offset maps including fixed-Type fields; zero-sized destructor order |
| Callable storage | Empty/inline/heap and zero-size over-aligned environments; capture order, direct-syntax versus general-expression allocation order, consuming partial cleanup, repeated Shared calls and common-value Move; consistent entries/adapters and safe allocation elision |
| Metadata and sharing | Full ArgKey versus payload CoreId, token collisions/retokenization, metadata/context schemas and lifetime, zero-count/null-entry destruction, partial/range cleanup, recursive keys and resource limits, exact destructor/context sharing keys |
| Safe value borrows | Immediate slot versus loaded referent, zero-size substitute addresses, call-wide ref/uniq protection and attributes, duplicate shared arguments, static/indirect/cleanup reentry, no unproven element alias attributes |
| Generic entry ABI | Explicit selection at budget zero; concrete scalar entries, partly fixed unresolved signatures, function values, opaque callee schemas, external contracts and candidate rollback without caller-group specialization |
| Generic context and fixed facts | Integer slots and adjacent operation pairs; slot deletion, different callee schemas, finite mutual recursion versus growing keys, full-width u64 identity; equal sizes with different alignment/destruction/derived layout; SharedReadResult and attribute proofs |
| Generic scratch frames | Copy-source separation, Loans/Partial Move, exact per-substitution capacity independent of added instances, zero-capacity adapters, recursive/loop lifetimes, nonescape, zero-size storage, alignment limits, stack probes and unwind |
| Generic lengths and destruction | N = 0, stride = 0, negative index, formation versus ordinary arithmetic overflow, body-slot versus destruction-context lengths, reverse destruction and count * N overflow without a new Abort |
| Generic budgets and reuse | Exclusive all-member assignments, shared/small-specialized coexistence, body work moved into entries/helpers, post-selection deduplication, bounded exploration, enumeration/cache independence, multiplier-only reuse, use-site Loans and product/test budget isolation |
| Weak and objects | No empty constructor; Option absence versus expired Weak; complete dependencies/Owned cyclic payload; builder cleanup before publication; all-mode payload +16, count limits and migration, upgrade/final-release races, guard lifetime and no freed-pointer reads; weak-memory ordering proofs separately from IR/native tests |
| ABI and ownership | Scalar/aggregate/zero-size passing; separate Copy source; acquisition transfers; result secured before cleanup; Abort/divergence before delivery; partial construction/Move; literal backing never freed; Heap ownership released once |
| Owned string lifetimes | Local-to-temporary Move, self-assignment, replacement after conditional Move, and declaration resets across loop backedges; destruction precedes placement and its live-state update; abrupt assignment operands produce no placement; conditional cleanup preserves scalar phi predecessors |
| String comparisons | All six operators, exact UTF-8/NUL content, empty values, unsigned byte order and length-first equality; shared inspection from Read through comparison, duplicate/nested Loans, right-operand effects and deferred cleanup, transfer operand acquisition before Loan abandonment, and ended Loans absent from checking continuations; temporary cleanup after Boolean capture, including skipped and repeated short-circuit operands |
| Match execution | One Subject acquisition; source-order first match; covered arms checked without runtime arrivals/updates; final-arm success proved by coverage; fitted integer and decoded string literals, bool/Unit/empty-string tests; binding declaration/acquisition only after selection; Subject storage reuse, arm cleanup before remaining Subject destruction, secured results and actual phi predecessors; Abort/divergence and Never subjects do not hide unsupported arms |
| Owned aggregate results | Secure before cleanup and deliver only on normal arrival; every arrival matches its acquisition; nested result transfers, conditional self-replacement, unconsumed-result cleanup, and loop/deferred storage reuse; Abort/divergence after securing does not cause enclosing consumption or destruction |
| Owned function calls | Logical argument order with named arguments and omitted Unit storage; caller cleanup in reverse acquisition order versus callee cleanup in reverse parameter order; conditional parameter lifetimes; results initialized only on normal return; nested calls, discarded results, shallow recursion and return snapshots across defer. For slot-based implementations, verify fresh result storage at each call, distinct argument/result storage within that call, correct physical addresses and no premature result forwarding |
| Instructions | All checked integer boundaries, signed minimum/-1, invalid shifts; float-to-integer boundaries and adjacent floats for every supported pair, NaN/infinities/fractions/signed zero; finite-to-infinity conversion; i128 supported/unsupported operations and helper dependencies |
| CFG and optimization | Short circuit/guards, scalar/aggregate/Never joins, actual phi predecessors, assignment/setter evaluation count, self-assignment, first placement, direct construction, edge cleanup/defer flags, nontermination, partial values/padding/overlap |
| Constants and FP | Exact reread fitted bits and UTF-8/NUL lengths; literal sharing; NaN Equatable distinction; subnormals/ties/signed zero; ABI-standard MXCSR after external save/change/restore, without comparing status flags |
| FFI and pointer operations | Clang C signatures; signed small integers, maximum unsigned values, mixed FP/integer and 5+ arguments; symbol/library/ABI collisions; null zero-displacement and valid positive/negative/one-past arithmetic; no expected result for unsafe violations |
| Runtime failures | Partial/zero-progress writes, missing stdout, failed stderr without recursion, allocation/free failures, size/address overflow, invalid releaseKind, empty Static/Heap strings, source provenance and ASCII path diagnostics |
| Backend supply | Native COFF only, disassembly and dependency inspection, no self/cyclic libcalls; memcmp unsigned first-difference results, equal ranges, null zero-count and alignment/guard boundaries; memcpy/memmove/memset boundaries/alignment/results and both overlap directions; __chkstk page/multipage frames, guard probes and register/stack preservation |
| Profile and publication | CPU/features independent of host, PIC/ASLR at ordinary 64-bit image bases, .pdata/.xdata consistent with prologues, one strong _fltused in both output kinds, package/ABI/hash/supply mismatch, unknown dependencies, mixed publication/hash failure |

Use fault-injecting test adapters where needed without expanding the production six-operation/seven-API set. Compare O0/O2 stdout, stderr, exit status, and observable effect/cleanup order; compiler Debug/Release must preserve acceptance and runtime checks. Run nontermination tests with bounded test-harness time and inspect optimized CFG. Separate required constant evaluation from ordinary constant expressions, and diagnose unsupported generated operations before optimization even in unused bodies.

Adopt helper supplies only after /NODEFAULTLIB custom-entry O0/O2 links and execution establish no hidden CRT/dynamic/TLS/exit-handler dependency. Check all actual object undefined symbols, including references added/removed by LLVM, against real providers. Version/hash/catalog mismatches fail validation. Validate memory intrinsic expansion and libcall paths, and assembly unwind information.

The first executable must itself produce exactly `Hello, world!\n` (14 UTF-8 bytes), empty stderr, and exit code 0; host test-runner output is not a substitute. Also test empty strings, Japanese/NUL bytes under redirection, and Abort code 1. Library verification checks retained pre-optimization bodies/signatures/linkage and object generation, not external linking or execution. Assess unnecessary slots/transfers/flags with representative O2 code without deleting semantically required work.

## A.15. Control-flow verification

Preserve Body form, evaluation context, transfer target, result sources, and the distinct analyses in §14.9. Verification must cover:

| Area | Required coverage |
| --- | --- |
| Layout | Reserved do and ordinary block identifiers; both Body forms for every executable construct; header baseline and wrapped headers; delimiter regions; nested-if grouping; match arm indentation; permitted directives in nested indented bodies and function-leading Constraints; missing items; rejection of old body colons and standalone `=>` lines |
| Results | Discarded body values versus explicit transfers; Unit-fixed targets; omitted else; loop exits versus iteration ends; dead result sources; delayed numeric defaults through nested results; declared results versus inferred Never |
| Anonymous functions | Explicit/common/unique expectations; Unit fixed by another argument independent of order; no body rechecking, instantiation-time reinterpretation, or result-discard overload priority |
| Transfers | Nearest-target lookup before context/Type checking; named operand colons/grouping; label activation only in bodies; yield through loops; function/defer barriers; discarded-selection bare-yield rejection |
| Paths and cleanup | Shared structural paths without literal pruning; while true versus loop; condition cleanup before branching; result acquisition before cleanup/delivery; defer self-exit resumes pending cleanup; cleanup divergence does not change the checked Type |
| Diagnostics and tools | Unexpected inferred Unit through nested discarded selections/do expressions; one warning per discard under §17.4's priority, including Copy/Non-Copy Result and separate arm occurrences; effect-free discard without assuming call purity; no warning solely for a final defer; formatter preserves Body form; refactorings preserve results, targets, and destruction order |

Coordinate with the cleanup, refinement, and Pattern checks in A.7, A.9, and A.10.

## A.16. Dependencies, artifacts, and bounded reuse

Implement [§18](../18-modules-and-dependencies.md), native connection rules (§20.8.2), and product/test planning (§21.3.7) without changing acceptance or selection based on processing order or cache presence.

Stream hashes from fixed input bytes instead of constructing large concatenation buffers. Intern/share each dependency's declarations, strings, Types, Origins, and verification facts; use range-checked integer references and reconstruct diagnostic paths on demand instead of copying paths per node. Share immutable lexical data only when its language conditions agree; never share mutable Binding/Koto state across definition environments or target/mode checks.

Use reverse dependencies and bounded worklists for invalidation. Bound graph size, work, concurrent verification, and total memory; do not make module depth depend on host-stack recursion. Valid semantic caches may load bodies lazily and avoid source decompression from integrity-checked managed input, but only after checking mandatory verification completion, including unused definitions. Reuse native copies/indexes by content. Compare alternatives with identical input and independent environment-specific state.

Common generation may deduplicate equal plan keys, direct-call statically selected implementations, propagate constants, and omit unnecessary code/metadata after verification. Do not demand full specialization for every Type or unconditional forced inlining. Introduce fine-grained reuse, laziness, and parallel checks incrementally; preserve the same no-cache semantics.

| Area | Required verification cases |
| --- | --- |
| Identity/resolution | Multiple paths/aliases, same-release conflicting content, distinct versions/Types, cycles, hidden transitive names, unique source selection and corrupt indexes |
| Locks | Source edits and relocations without rewrites, changed dependencies, missing empty locks versus stale existing locks, test-only failure, both-partition validation, concurrent restore |
| Pack/publication | Repeated same-version trials, destination-scoped conflicts, whole-closure diagnostics, explicit settings, existing Package closure, all-environment failure, atomic table update/interruption |
| Storage | Unicode collisions, differing valid compression, whole-archive fast checks and full-validation fallback, corrupt entries/caches, store verify, pin/collection races |
| Semantic reuse | Cross-version correspondence without Type merging, same/changed private effects, changed absence facts, Proven withdrawal, recursive components, observed versus unobserved source edits |
| Tests/generation | Unrelated tests, new generic substitutions, same-release conflicts, changed implementation dependencies, initialization/cleanup closure, unchanged product sharing/frame/budget choices |
| Native | Self-targeted combined configuration, short/long import objects, mixed archives, unused duplicate members versus ambiguous required symbols, directives, stale summaries |

Measure retrieval, lexing, semantic verification, generation, and linking separately; record bytes read, hash passes, revalidated judgments, bodies loaded, allocations, peak memory, code size, and runtime. Include many-path graphs, a large Library with sparse use, generation-only setting changes, and repeated small external/generic calls. No measured speedup or numeric resource guarantee is implied by these rules.

## A.17. Test verification and runner requirements

Implement the contracts in [Test definitions](../06-declarations-and-containers.md#651-test-definitions), [verification operations](../17-failure-handling.md#175-test-verification-operations), [discovery/CLI](../20-compilation-configuration.md#209-test-command-and-discovery) and [execution/reporting](../22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting). Unsupported operations must be diagnosed before generation, not silently accepted. Declaration parsing does not establish test execution support.

| Area | Required verification |
| --- | --- |
| Membership/discovery | Test additions cannot change product lookup/layout/conformance/generation; check generated membership, all test bodies before filters, ordinary-build exclusion, invalid Attributes/signatures, forbidden direct calls and function values |
| Verification | Single evaluation, short circuit, lazy message evaluation, outward message transfers, failure-branch Moves, snapshot time and no added Copy/Loan/lifetime effects; expect failure continues and require failure Aborts; both operations in test-only helpers with Unit/non-Unit results, nested functions, Closures and defer bodies; product-body uses rejected |
| Cleanup | Condition/message temporary cleanup before require's Abort; no enclosing defer, remaining-local destruction or normal static shutdown after Abort; additional expect failure/Abort during cleanup, nested verification in messages, nested static initialization and shutdown phase attribution; no active-case misuse accepted |
| Identity/protocol | Repeated/nested sites use distinct IssueIds, basic-only failures survive require's Abort, no duplicate verification failure for its termination, reliable require-Abort identification versus an earlier cleanup/message Abort, ID exhaustion/count saturation, stale ArtifactId, corrupt/missing completion, exit zero without valid completion |
| Bounds/recovery | Large logs and failures, limits preserving evaluation, independent control space, broken communication, retained descendant pipes, normal/abnormal/cancelled finite recovery, original reason plus recovery errors |
| CLI/artifacts | Unknown IDs, empty selection, conflicting options, stable list/display order, all-case artifact reuse across filters, one process per case and parallel limits |
| Performance | Debug/Release semantic agreement, successful checks without failure allocations/events, reusable buffers, one startup artifact validation with per-child ID matching; measure costs under §22.6.5 |

Concrete formats/defaults are tracked in [Appendix D.4](D-deferred-features.md#d4-testing-profile-details-and-extensions) and must be settled before implementing their interfaces. Membership/discovery, serial verification/reporting, cleanup/recovery, then parallelism/external output are a possible implementation sequence, not reduced conformance requirements.
