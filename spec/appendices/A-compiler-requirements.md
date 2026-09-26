# Appendix A. Compiler implementation requirements

UTF-8 formatting, buffers, interpolation and Console optimization must satisfy the [formatting profile](../utf8-formatting.md), including its required costs and [verification matrix](../utf8-formatting.md#7-conformance-verification). Preserve original exclusive Loans in shared dependent results and check BufferWriter's effect bound before erasure.

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

Retain the normalized argument-name contract (external names and K, §7.2.2) separately from source boundary position, Types, receiver position and defaults. Check unique external names across both sections and the receiver, independently of internal-name scopes. Preserve contracts and inherited specializations through source round trips and version-checked reload; changed contracts invalidate dependent call plans.

Cover absent/leading/intermediate boundaries, trailing commas, nested defaults and body dedents; reject repeated/empty boundaries, adjacent commas, removed suffix markers and boundaries in prohibited contexts. Test all name/default combinations, alias names, duplicate/unknown/missing arguments, positional prefix matching without skipping or Type-directed remapping, bound/unbound receivers at every position, constructors/base calls, requirements, overrides, specializations, function values and foreign headers. Check equivalent source positions with equal K and prevent candidate merging when K differs. Verify source-order explicit evaluation, declaration-order defaults and cleanup through supported generation. Reject mixed language versions and incompatible old snapshots.

As implementation guidance, precompute positional limits with receiver handling and reject excess positional supply before inference without hiding declaration errors. Reuse name-to-slot lookup per declaration and generic family; benchmark small linear scans against cached indices rather than allocating a dictionary per call. Name lookup never reorders evaluation. Measure allocation and timing on representative cold/warm cases without claiming universal zero allocation.

Pending conformance mappings are not proof evidence. Resolve information needed for lookup before computing effects; an empty or missing summary cannot fill unresolved work. Complete implementation proofs, public statuses, conformance, and use obligations in dependency order. Effect closure cannot justify a cyclic conformance, and no unresolved ObjectCallCompatible status may be published. Preserve ordinary errors separately from a valid implementation's NotProven status.

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

Validate direct/child Place permissions, implicit receiver acquisition, the slot borrows and dereference of §13.5.5 and the common adaptation of §10.2 under the lending rule (§7.3, §15.1.5), generic Copy proof, the transfer operation `@move` on Copy and Non-Copy Places, first-placement history, construction-call bans, getter temporary restrictions, and normal cleanup before lowering. Preserve Contract result restrictions through witness optimization. Update parser, writer, grammar, serialization, diagnostics, and artifact invalidation for accessor syntax, the transfer operation and the dereference.

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

Verify OwnedOrigins through raw pointees, unused Type/Origin slots, captures, and recursive payloads; test `a outlives static`, unresolved abstract Origins, Function Item bound arguments, and fixed callable signature Origins versus per-call binders. Reject mutable-static borrows at Owned boundaries, including after capture and across modules. Checked casts may supply only proof-covered fixed Origin bindings as static; preserve outer handle Origins and never bind per-call callable Origins. None of these checks may depend on a private body being available to a client.

Verify input-derived Callable results for all three receivers: per-call Origins, multiple-input meets, retained result Loans, rejection of a shared-to-exclusive upgrade, and rejection of hidden-receiver or call-local result dependencies. Common Function Type compatibility uses the same argument/result relation while retaining its separate Owned-environment restriction.

Retain capture bindings, acquisition order/effects, mutability, nested dependencies, environment identities, and capture/call/result Origins and Loans through compilation and artifacts. Resolve required Copy, receiver, Callable, and erasure obligations before finalization. Check concrete Copy independently of receiver kind and erased-container classification.

Verify §12.4.4's single public status per call operation, standard Place/witness distinction, and common implementation-family guarantee. Cover Whole/Base replacement, incomplete MoveOut, exclusive escape, permitted Part replacement/exchange, reference-field versus referent aliases, captures/statics, defaults/cleanup, returned borrows, and separate/indirect/generic calls. Verify recursive fixed points without treating circular conformance as evidence. A NotProven specialization changes the public guarantee without a declaration error for that reason; its cause must be traceable at a failing use. Unknown, missing required data, and unfinished verification cannot supply proof.

Verify multi-level generic base projection, original protected-receiver checks, shared/exclusive access, returned-Loan anchors, direct Field access, and rejection of whole-base replacement, consuming receivers, and unbound derived/base argument conversion. Preserve these results across separate compilation and optimization.

Verify object creation's Copy/Move input states, payload eligibility, fresh identity, initial strong count, allocation failure, and source-storage responsibility. Strong-owner duplication must preserve identity without retaining a Loan on source-handle storage; cover count overflow, release through different views, exactly one final cleanup, and resurrection rejection.

Test capture versus call acquisition, unused/nested explicit captures, let/var, reference/referent lifetimes, Copy/Move-consuming calls, every Callable receiver, per-call Origins, variant cast Loans, static inherited calls, and construction/destruction restrictions. Verify public status, use legality, complete dynamic cleanup, and receiver adjustment independently of load order and optimization.

Verify implicit receiver acquisition (§7.3) against its explicit spelling by semantic case, not by the absence of a spelling: equal evaluation order and count for receivers with side-effecting indices or getters, equal reservations (shared reads during reservation accepted, overlapping exclusive acquisitions rejected), equal retained result Loans, equal object paths (no ObjectCallCompatible proof for a Sealed payload dereference, Proven required for a protected object path), and no additional Copy, heap allocation or reference-count operation. Accept bare exclusive receivers and redundant explicit spellings, preserve the effect of a different explicit spelling, and reject bare owned Places at exclusive non-receiver positions, including Places reached through exclusive references. Diagnose the main causes of §7.3 and receiver-shape violations in Type declarations, Contract requirements, specializations and constraint-gathered groups, naming every mismatching declaration.

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
| Binding-set relations and `View<T>.Some` | Equivalent completed bindings; reject an Origin suffix directly in the Case qualifier |
| Unconditional Copy with unconstrained T / constrained T / only ref/T payloads | Declaration error / valid derivation / no referent Copy premise |
| Conditional Copy when T is Copy | Type remains usable for non-Copy T; Copy is available only when its condition is Proven |
| Kimi Option/Result conditional Copy | Check both Result payloads, nesting and complete Semantics; active Case does not change capability. Verify reuse versus Move, Unknown versus Refuted, and borrow dependencies after Copy |
| Kimi condition-atom sets | Accept both orders, grouping, and duplicate atoms; reject missing/unconditional Copy, missing/extra atoms, and wrong Symbol Identity. Check generated and loaded definitions without spelling-based user-enum behavior |
| Copy payloads through shared Subjects | Bindings are `ref/T` even for Copy payloads; `@deref` Copies the value; explicit element `@ref` retains storage/Origin and tryGet keeps its reference result. A generic head with plain `s[0]` fails definition checking |
| Child structural Patterns, Grouping, `ref/ref/T`, `ref/uniq/T`, `uniq/T` payloads | Repeated selection until the required structure; shared access bounded by any shared layer; exclusive bindings only on exclusive paths; Unit Patterns through `ref/(ref/())` |
| Same Non-Copy payload Type on owned/shared/exclusive paths | Body acquisition is transfer, `ref/T` or `uniq/T` respectively; test string, object, and exclusive-reference payloads |
| Subject written bare, `x@ref`, `x@uniq`/`x@objuniq`/`x@uniq/T`, `x@move`, `x@owner`, a temporary, or a reference value | Ordinary acquisition except that a bare Place is borrowed; Shared, Exclusive or ByValue mode by the access of the Subject Place: an owned temporary is ByValue, a `uniq` value Exclusive even when transferred or returned, `@uniq` on a `let` slot is rejected, a Copy Subject under `@move` is transferred and `x@owner` copies it |
| Guard candidates | `ref/T` for every stored Type and mode; `candidate@ref` borrows the guard slot; rejection of assignment, Move, capture and escaping candidate Loans |
| Non-Copy ByValue Subject (`x@move` or a temporary) with only `_`; false guard followed by acquisition | Initial whole transfer into the Subject Place even without bindings, while a bare Place Subject is borrowed and stays usable; preserve payload for the next arm without double destruction |
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

Test struct-conformance inheritance under §8.4.4: a compatible Utf8Format-like mapping succeeds; Self arguments/results and owning receivers fail that path without invalidating the derived declaration. Cover fixed associated-Type normalization, alternative paths, intrinsic exclusions, and Unknown/Error distinctions. Across A -> B -> D, retain A's Member Identity/Self and composed Type/Origin/receiver mapping. Diagnose explicit conformance and constraint-use failures with the failed requirement/cause, without warning on the open base. Generic calls use the retained mapping, not caller-side member lookup.

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

Parse each brace group once, then validate its schema or binding-set role (§15.3.1). Preserve explicit empty headers, postfix borrow attachment, set labels and attached relations through parse/write/parse and artifact reload. Reject removed callable lists, mappings and brace borrow annotations. Cover headerless single-slot discovery, typo-induced second names, nearest-scope wrong-role errors, isolated field sets, split closed headers and intermediate qualifier obligations.

**Borrow suffix output and diagnostics.** Emit `ref/T? during a`, `during (a and b)` and `ref/(uniq/T during b) during a`, with required function/adaptation grouping. Write body, optional suffixes and annotation together; never emit `ref/T during a?`. Retain annotation and target source positions without breaking parent/child ranges. These requirements add no general diagnostic-string reparsing or serialization format guarantee.

Distinguish missing targets, ineligible Semantics, suffix order, Origin expressions, use positions and name-resolution errors. Guide invalid grouped suffixes to `ref/T? during a`, and Type-only `from a` to `during a`. For a Type-only `during a and …`, explain intersection parentheses. In a requirement conjunction, if the right simple name/projection is invalid as a requirement but valid as an existing Origin, add the same hint without rebinding or reparsing. If both roles are valid, a lint may warn; explicitly grouped `T is (ref/U during a) and B` suppresses that lint. Newline hints apply only to erroneous annotation-like continuations, never valid ordinary Names.

An owned value's bare name cannot denote an outer borrow Origin even when it carries internal dependencies. Suggest a declared `x.slot`, or a local Borrow with inference such as `let r = x@ref` when storage borrowing is intended. `during (x@ref)` is not an Origin expression; no suggestion extends a lifetime. Result-default diagnostics follow §15.4.3.

For erroneous `Name{…}/…`, explain the removed borrow spelling while preserving the selected parse. In adaptation, supplement a syntax/Type error only when the head's Semantics role is established; `x@s/T` may infer a borrow Origin, but `x@(s/T during a)` still cannot prescribe one. Valid set-plus-division expressions receive no legacy warning. Reuse the once-parsed brace range and its immediately following token, not a new `{…}/` lookahead scan. Only this additional shape check is O(1); Origin parsing and lookup retain their normal costs. Attachment can retain the current AnnotatedType's first Semantics directly, without grouping traversal or a duplicate-state table. Allocate explicit source-annotation data only where needed, and never mutate shared resolved Types with occurrence-specific syntax.

Verify equality substitution before result completion, normalized nontrivial explicit outlives relations, and deterministic fallback for result equality classes. Test tautologies, both orientations of equality, upper/lower/composite bounds, declaration-fixed result quantification, and rejection of nested callable input quantification. Composite right-hand meets must not become independent edges. Verify local initializer inference, equality-complete declarations without initializers, and no reopening from later assignments. Fields may not introduce hidden public preconditions, and Phantom Origins may not manufacture Loans.

Retain §15.3.7's canonical contract, including independent anonymous slots for each input Type occurrence, conditional reconstructed-pair slots and fixed nested dependencies. Apply stored/specialized contract inheritance before omission. Verify anonymous versus explicit correspondence, rigid required binders, fixed `static` counterexamples, nested signature boundaries, Owned-based aggregate result defaults and absence of body-derived input conditions. Origin conditions must survive rechecking, separate compilation and cache invalidation; inactive slots must not erase inner dependencies.

Lower and emit only after Origin/Loan obligations are verified. Test positive execution and rejected escapes/conflicting mutation with omitted aggregate Origins, including nested occurrences and multiple inputs. Origins add neither runtime payload fields nor machine-code copies solely for different Origin bindings.

Verify declared Origin bounds at both definition and use: forward binder references, unknown/duplicate binders, equality cycles, `static`, substituted call/Type bounds, fragment agreement, nonstrengthening Contract implementations, and inherited specialization bounds. Bounds never supply a Loan or add overload candidates. Distinguish a local borrow's carried Origin from its slot's inferred Origin; reject bare owned-local names as Origins and preserve independent nested dependencies.

Fixed-array verification must cover contextual of/length, explicit slot kinds and conflicts, parenthesized constant expressions, constant-readable eligibility, expected-Type boundaries, candidate-local literals, element counts, zero length, zero-sized elements, recursive/nested layout and stride, normalized dependent expressions, negative intermediates, public ValidLength failures, universal body checking, specialization keys, and artifact invalidation after constant changes. Check whole initial construction, static-path reinitialization after Partial Move, reverse partial cleanup, and literal-only Move Paths.

Verify full-Type binding, one-slot pair decomposition, reconstruction versus applying s elsewhere, duplicate names, trailing commas, occurs-checks, invalid arguments, recursive storage, and input-order-independent principal Origins. Origin simplification must preserve distinct Loans.

Test specialization ambiguity before contract checking; inherited defaults/Safety; closed arguments; Origin-only duplicate keys; obj/D versus rc/D; mandatory selection through shared callers/references; recursion; and no fallback. Verify unused selected declarations, closed-set ownership, and set/body invalidation. Code sharing, budgets, automatic specialization, and LTO must preserve behavior. Cover unconstrained reuse/capture rejection, fixed Property results, element Places without Copy-dependent result Types, rejection of bare acquisition without Copy evidence, caller-independent definition checking, and no hidden applicability conditions. Instantiation preserves §8.10’s symbolic ownership/cleanup plans.

## A.13. Sequence access verification

Bind index versus range access by argument Type, not literal syntax. Retain receiver/element Places, access form, bounds, permissions, acquisition, complete result Type, and Origins/Loans. Nested access cannot read/copy intermediate arrays. Element Places carry the stored Type; validate every admitted generic case before lowering.

Preserve one-time receiver/argument evaluation, index-evaluation protection, exclusive activation after bounds checks, and RHS-first simple and compound assignment. Apply common increment/exchange rules. Metadata snapshots add no source Loans and waive no receiver validity checks.

| Verification area | Required coverage |
| --- | --- |
| Index and Range | Lengths 0/1, ^0/^1, negative offsets, excessive distances, saved values, half-open/inclusive/reversed ranges, operand evaluation before construction checks |
| ResolvedRange | Maximum-isize end, finite and permanently exhausted iteration, rejection of direct Range iteration, reuse against shorter/resized targets |
| Place acquisition | Bare Copy versus `@move` transfer (including Copy elements), rejection of a bare Non-Copy element without an expected borrow Type, literal-only eligibility, implicit borrows at expected Types, nested writes without intermediate Copy, reinitialization after Partial Move |
| Indexable Contracts | `index` versus `indexUniq` selection from the use, several `Key` conformances, user conformances publishing Place results, rejection of exclusive selection through shared paths |
| Slice boundaries | Zero-based reslicing, split endpoints, None from each try-prefixed API, failure inside arguments remaining Abort |
| Lifetime and storage | Borrowed Array/Slice retention, temporary/local escape, copied references versus slot borrows, nested Origins, whole-array Loans including empty/split views, rejection of mutable-static borrows at Owned boundaries |
| Metadata and iteration | Receiver effects, completeness checks, no result Loan for metadata, stable saved indices, reference items independent of element Copy, items independent of the iterator's storage; `ref`/`uniq` Array, fixed-array and Slice Subjects entered through their referents with Loans kept through the loop; `@uniq` Subjects yielding `uniq/T` items with non-overlapping element Loans |
| Lowering | Identical acceptance, results, effect/Abort order, and Loan legality with optimization enabled/disabled; O(1) view operations without element-proportional allocation or Copy |
| Dynamic mutations | §4.7's success/absence/duplicate outcomes, ^0/^1 and maximum-length cases, RHS-first Dictionary replacement versus argument-first insertion, stored-key identity and remove/re-add order |
| Capacity and cost | No internal allocation within capacity, including deletion churn; reserve additional arithmetic and overshoot; shrink failure preserves placement; growth/reindex operation counts meet amortized bounds; cleanup-free Array clear is O(1) |
| Dependencies and effects | No dependency subtraction after clear/None/Err, old-result versus new-input dependencies, uniq element conflicts, receiver reservation before arguments and activation before entry, generic equality/destructor summaries and static reentry |
| Temporary argument borrowing | Typed/generic/unfitted inputs, candidate ambiguity, normal temporary expiry and rejected escape, no implicit extra reference/handle layer or exclusive-temporary extension |

## A.14. Layout, LLVM, and runtime verification

Validate windows-x64-v1 with LLVM 22.1.8, distinguishing semantic acceptance, TypeLayout, IR structure, object ABI/dependencies/unwind, and actual execution. The verifier alone cannot establish layout, foreign ABI, ownership transfer, or correct startup. Keep input/target/settings/expected results together; retain representative golden IR plus structural checks, and normalize only irrelevant internal numbering/paths.

Internal-function IR/signature expectations test the selected compiler implementation, not a stable language ABI. When its choices change, update those expectations and verify matching definitions/calls/adapters, artifact invalidation, source-order acquisition, ownership/result delivery, and unchanged external contracts. No cross-build internal ABI compatibility test is required.

The rows **Metadata and sharing** (its sharing parts), **Generic entry ABI**, **Generic context and fixed facts**, **Generic scratch frames** and **Generic budgets and reuse** apply to deferred generic code sharing (§21.3.1). The initial monomorphizing profile verifies explicit selection, generic lengths and destruction, and generation limits on its concrete bodies.

| Area | Required coverage |
| --- | --- |
| Startup | Unique implicit/explicit body; uninitialized top-level let/var and Unit; empty/declaration-only documents; invalid/duplicate main; mixed forms; selected/generated items; Library restrictions; static-only documents |
| Layout | Attribute errors and fragment conflicts; single C storage fragment; moving the entire fragment/renaming method fragments; nested/zero-sized/aligned Types; generic substitution; inline cycles; overflow; C size/alignment/offsetof under packing 16 |
| Aggregate order | Alignment sorting with logical-order ties across own Fields, captures, Tuples and Case payloads; fixed base/tag/array positions; whole-instantiation offset maps including fixed-Type fields; zero-sized destructor order |
| Callable storage | Empty/inline/heap and zero-size over-aligned environments; capture order, direct-syntax versus general-expression allocation order, consuming partial cleanup, repeated Shared calls and common-value Move; consistent entries/adapters and safe allocation elision |
| Metadata and sharing | Full ArgKey versus payload CoreId, token collisions/retokenization, metadata/context schemas and lifetime, zero-count/null-entry destruction, partial/range cleanup, recursive keys and resource limits, exact destructor/context sharing keys |
| Safe value borrows | Immediate slot versus loaded referent, zero-size substitute addresses, call-wide ref/uniq protection and attributes, duplicate shared arguments, static/indirect/cleanup reentry, no unproven element alias attributes |
| Generic entry ABI | Explicit selection at budget zero; concrete scalar entries, partly fixed unresolved signatures, function values, opaque callee schemas, external contracts and candidate rollback without caller-group specialization |
| Generic context and fixed facts | Integer slots and adjacent operation pairs; slot deletion, different callee schemas, finite mutual recursion versus growing keys, full-width u64 identity; equal sizes with different alignment/destruction/derived layout; element-Place plans and attribute proofs |
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

The [test execution profile](../testing-profile.md) defines concrete formats and defaults. Verify solution-wide barriers/budgets, stable single-project/solution IDs, absent product entry, nonexecuted but checked startup bodies, temporary API/environment, and bounded result retention. Membership/discovery, serial verification/reporting, cleanup/recovery, then parallelism/external output are a possible implementation sequence, not reduced conformance requirements.

## A.18. Kimi library and source aliases

Verify the rules in §18.1, §9.4.1 and §22.1/22.4 across parsing, Binding, analysis and generation:

| Area | Required checks |
| --- | --- |
| Library and output | Reserved Kimi, ordinary Core, no old forwarding names, Console's direct membership, identical function Identity through qualification/open/named aliases, function values, ownership, UTF-8/LF and Abort behavior. |
| Environments and storage | Mandatory defaults in root/dependency/generated documents; explicit/default precedence; empty versus redundant Kimi additions; actual byte IDs versus normalized settings; source-package round trips. |
| Resolution | Optional `::`, order independence, no alias chains even inside arguments, original declaration environments, invalid targets, all path access/formation/condition/Origin obligations. |
| Lookup and diagnostics | Reference identity including bindings; duplicate/conflicting mappings, opened-member ambiguity, Type/Value paths, no fallback, root-hiding warnings and all warning exclusions. |
| Boundaries and reuse | Document/fragment isolation, no re-export or added authority, pending obligations retained through sharing, final completion after generation, invalidation on changed inputs; identical results with caches enabled or disabled. |

Index named aliases per document and reuse Container member indexes for opening aliases. Share fixed references and normalized bindings without conflating them with completed validation. Resolve paths once per valid Binding generation; share default environments by defining module. Preserve use-site checks and invalidate results when assumptions or dependencies change. Library implementation names should identify Kimi (for example, KimiLibrary); IntrinsicKind remains a property classifier, and processing names such as BuildCore are unrelated.

## A.19. Complete payloads and whole-value updates

Verify the intrinsic Sealed Identity, normalized outer owner Core, open/Never rejection,
outer-only classification, and four-valued proof rules. Verify the intrinsic ObjectPayload
Identity, its direct and premise-based judgments, the `Self is not ObjectPayload` opt-out
and its inheritance, Object Target evidence including pair evidence from admitted Semantics
sets, and the rejection of every object form, creation, upcast, cast and `is` test over an
opted-out Core (§8.4.7.2). Check generic signature formation before body generation; no
missing capability or Origin proof may become a deferred layout obligation. Recheck both
positive and negative evidence after openness, opt-out, Type formation, effect, or
specialization changes.

Test every §13.5.5 payload dereference, exact internal Type/Origin identity, outer
lifetime shortening, owner retention, parent suspension, shared coexistence,
count-one rc/arc rejection, and explicit versus implicit receiver paths. Preserve
protected base calls, defining Self, Property permissions, witness Identity,
ObjectViewCompatible, and public ObjectCallCompatible status.

For `replace`, `exchange`, and `swap`, verify original declaration Identity, textual
argument order including names, early target reservations and simultaneous activation, full initialization, exact
Types, and structural disjointness. Test scalar and non-Copy contents, `let`
fields, open ordinary owners, nested references, independent external dependencies,
old-content dependencies, and returned destruction responsibilities. Verify
destruction at the original location, Abort/divergence before placement, and
absence of destruction during exchange/swap transfers.

For §15.6.7, test implicit and direct explicit reservations, transparent parentheses,
object projections, generic/indirect/Callable calls, constructors and prepared defaults.
Check overlapping exclusive reservations, shared arguments retained at activation,
ancestor authority, disjoint static paths, nested dependencies and cleanup effects.
Storage, captures and control-flow results must not propagate reservations. Test
abandoned calls, locally caught transfers, Never/Abort and unreachable checking.
Corrupted reservation/activation plans must not reach emission; O0/O2 must preserve
evaluation order, cleanup and the call-wide attribute contract.

Payload tests must retain Identity, allocation, Dynamic Type, header, counts, and
Descriptor; final release destroys the current contents once. Compare O0/O2 and
shared/specialized generation, including old-content refinement invalidation.
Lowering and Emit consume checked address/acquisition/transfer plans and reject
stale or incomplete plans; never rediscover completeness from optimized code.

## A.20. Declaration Container nesting

Verify the following after directive selection and generation, including serialization/rebinding and dependency invalidation:

| Area | Required checks |
| --- | --- |
| Placement and names | Recursive nesting, prohibited bodies, rootgroup, fragments, nearest Self, explicit receivers, namespaces/access, fixed outer arguments |
| References and Origins | Unused binding identity, trailing/intermediate mappings, parenthesized Contract selectors, construction/Cases, OwnedOrigins, retained intermediate checks |
| Constraints and Contracts | Input/obligation/implementation roles, parent/child evidence, kind-preserving occurs-check, residual possible collisions, conditional merging including bases, declaration-path cycles |
| Inheritance and aliases | Original declaration identity and substitutions, inherited-name conflicts, fixed alias bindings, equal-reference deduplication and distinct-binding ambiguity |
| Static storage and generation | Origin-erased key, uniform validated plan independent of first access, entry/context reachability, lazy initialization/cycle Abort, Loans, reverse cleanup |
| Resources and reuse | Shared declaration trees and normalized references, deep nesting, converging paths, outer-change invalidation, resource diagnostics without changing proof acceptance |

Exercise both accepted and rejected programs. Parsing or reference interning alone is not evidence of lifetime, conformance, static-storage or executable support.

## A.21. Documentation Comments

### A.21.1. Implementation boundaries

Implement the optional [§2.3.1–6](../02-source-and-lexical-structure.md#231-documentation-text) path and the [Documentation Markdown profile](../documentation-markdown.md). Keep collection, association, text/source mapping, Markdown processing and publication separate:

```text
SourceDocument
  -> ordinary lexer: optional comment ranges
  -> Parser: declaration-fragment association
  -> on-demand text and source mapping
  -> independent Markdown syntax
       -> summary and item candidates
       -> declaration-dependent classification <- Binding facts
       -> HTML and links <- renderer settings and output mapping
  -> selected publication and optional documentation diagnostics
```

Use ordinary lexing, including interpolation expression lexing, rather than an independent regular-expression comment scanner. Keep source-ordered ranges per immutable SourceDocument, not per-line objects or extra executable tokens. Binding supplies declaration identity, effective access and specialization facts. Analysis, Lowering and Emit require no documentation metadata. Collection and dependency boundaries remain in §2.3.6; syntax lifetime, reuse and resource requirements are in [profile §5](../documentation-markdown.md#5-syntax-api-and-processing-guarantees).

### A.21.2. Correctness and differential tests

The formal profile is the authority. Use applicable official CommonMark 0.31.2 examples for unchanged rules and explicit expected results for profile differences. Markdig may be retained in tests/benchmarks as a comparison implementation; it does not define correctness. Pin comparison versions and settings, accounting for Unicode, URL policy, heading placement and rendering options. Normalize only documented, irrelevant layout differences; investigate other mismatches rather than accepting the comparison output automatically.

Assert syntax structure, item classification, node identity and source ranges directly, not only HTML output. If optional per-character mappings are exposed, verify them too. Cover these groups:

| Area | Required verification |
| --- | --- |
| Lexing and text | Recognized/ordinary/trailing comments, literals/interpolation/block comments, all line endings, EOF, empty text, whitespace, non-BMP text and original UTF-16 mappings |
| Association | Every target, same-line/multiline Attributes and their interiors, nearest/empty/misindented candidates, headers, scope/file boundaries and syntax recovery |
| Selection | Incomplete False #if syntax, reached #switch arms with nested exclusions, no migration to surviving declarations, no extra excluded-region parsing |
| Markdown blocks and inlines | Every retained feature and interaction; every [profile §2.4 difference](../documentation-markdown.md#24-omitted-syntax-and-boundary-examples); three/four-column starts inside and outside containers, marker widths and five-space list padding, explicit continuation, tight/loose lists, closed/unclosed fences and incomplete delimiters |
| Character processing | Escapes and numeric/five named references, code exclusions, literal fallback and no reparsing; pinned Unicode 15.0.0 across cultures/runtimes, whitespace/symbol/unassigned and supplementary characters, including classifications that differ across Unicode versions |
| Summary and items | First-block rule; plain/code names, decoded colons and exact whitespace, case and non-NFC names, duplicate/overlapping descriptions, root-only extraction, parameter/standard-name collisions, external/generic/Origin names, receiver roles, ambiguous/unknown and not-yet-classified candidates |
| Positions and publication | Exact half-open body/source ranges under LF/CR/CRLF, empty/EOF positions, decoded spellings and partial tabs; stable identity, concurrent requests, eager/lazy equivalence, interrupted work, retry and preservation of completed results |
| Reuse | Each [profile §5.2 dependency](../documentation-markdown.md#52-completion-concurrency-and-reuse), including identical text in different source paths/projects, declaration/receiver changes, parser/Unicode/settings changes, configuration selection, generated replacement/removal, serialization/reparse and uncacheable callbacks |
| Kimigayo integration | Fragment order/provenance and independent Markdown scopes, rootgroup leaf, associated Types, overloads/specialization, effective access, ordinary/generated sources, unresolved links and source-mapped diagnostics |

Link and HTML tests must cover the separate stages in [profile §4](../documentation-markdown.md#4-html-and-links):

| Stage | Required verification |
| --- | --- |
| Recognition and base | All reference kinds; allowed/disallowed and mixed-case schemes; ordinary/generated sources; display page versus HTML base; empty/query-only/fragment-only destinations; absent versus empty components |
| Logical resolution | Project identity, ordinary logical source names containing `%`, `?` or `#`, split-before-decode, valid/invalid UTF-8, dot segments, root escape, encoded separators/controls and exactly-once decoding |
| Output mapping | Changed page layout/base, unavailable mapping, independently encoded path/query/fragment, literal percent data versus existing URL escapes, first-segment colon, one leading slash versus `//`, and preservation of reference kind |
| Validation and display | Checks before and after mapping/rewriting, whitespace and controls, malformed percent escapes, backslashes, `https:` without `//host`, scheme-specific failures, attribute/text/code escaping, decorated disabled labels and original disabled autolink spelling |

Run targeted tests and existing lexical, Parser, source, Attribute and directive regressions, followed by ordinary builds and the complete managed suite. Compare tokens, language diagnostics, Binding, ownership, checked lowering and emitted output with collection on/off. Documentation-only edits may change positions or source-observing Mod results, but otherwise preserve program meaning. NativeAOT requires an explicit request. No new CLI, formatter, dedicated Mod query, `kimi:` link or doctest facility is required; an existing formatter must preserve text and association.

### A.21.3. Performance and resource verification

Before implementation or optimization, record the environment, input data, procedure and concrete acceptance gates in the implementation plan. Compare elapsed time and allocations for equivalent work; report differences in supported features and output separately. Measure:

- Short summaries, parameter lists, long code blocks, deep lists/quotes and incomplete or adversarial delimiters/links.
- Parsing, summary/item queries, source mapping and HTML separately; first use and repeated use, and the supported eager/lazy, cache and optional-diagnostic modes. Tests do not require implementing an otherwise unused optimization.
- Allocated bytes, retained-result memory and peak memory, alongside elapsed time. Include duplicate work from concurrent requests and interrupted/retried processing.
- Increasing input length, nesting depth and concurrent request count. Inspect for repeated scans, quadratic growth and stack exhaustion in parsing, extraction, output and diagnostics. Timing alone does not prove a complexity bound; review algorithms too. Internal work counters are optional.

Verify zero documentation-specific allocations with collection disabled and no dedicated candidate buffer when none is needed. Include an ordinary-compilation baseline; parser-only improvements do not establish a compiler-wide speedup. Keep external callbacks, declaration/source-name inputs, generated output and optional fine-grained mappings visible in resource accounting. No unconditional speedup or fixed parser architecture is required; observed costs must meet the profile's bounds and the recorded acceptance gates.


## A.22. Optional Types, propagation and discard

Implement §3.2.3, §14.2.4, §14.6 and §17.2.4/§17.4 consistently across lexing, parsing, Binding, control flow, ownership, lowering and generation.

- Preserve optional/try/discard syntax and source locations. Normalize optional syntax to recognized Option Identity before proofs, decomposition and layout.
- Check prefix/arrow binding, `?` before postfix `during`, repeated `?`, grouping boundaries, generic arguments and adaptation targets after Optional expansion. Include `@(ref/T during a)?`, reject outer-chain Origins and suffixes on Semantics shorthand, and verify ordinary contextual-keyword names and error recovery.
- Recognize standalone _ and try only after scanning the whole identifier; retain all specified underscore uses and reject ordinary names.
- Resolve try operands without expected Types. Check both Cases, one-layer extraction, distinct success/error Types, incompatible return targets, unreachability, nested boundaries, anonymous results, generics and Never.
- Route propagation through ordinary match acquisition and return cleanup before generation. Verify each evaluation and destruction count, retained borrowed payload dependencies, partial argument/aggregate construction, deferred cleanup and Abort/non-completion.
- Check explicit discard as a statement with an expectation-free Value operand, no lifetime extension and only local warning suppression. Verify Unit exceptions, nested Result, all warning priorities, independent internal discards and definition-time generic warnings.
- Unnamed iteration bindings keep acquisition, lifetime and cleanup; test repeated _, Tuple arity, duplicate names, whole-versus-component acquisition and early transfers.
- Diagnostics explain mismatched success/failure paths and valid alternatives without speculative fix-its or unlimited overload retries.

Reuse existing Type, Case, match, return and cleanup machinery without textual duplication or hidden Closures. As-if copy/slot elision must preserve acquisition/Move state, dependencies and destruction order. Cleanup sharing requires matching state, defers, secured results and destinations (§21.4.5), not merely equal scope depth. Do not assume failure paths cold without evidence, or add niche optimization beyond the nonnull Option representation of §21.1.5. Old caches/artifacts must not certify changed rules.


## A.23. Places, borrowing and iteration

Implement §3.4–3.5, §4.6.9, §7.1.1, §8.4.3, §10.2–10.3, §13.4, §13.5.5, §13.7, §14.6, §14.8, §14.9.1, §15.1.3–15.1.6, §15.6.3, §21.1.5 and §22.1.2 consistently across lexing, parsing, Binding, ownership analysis, lowering and generation. Parse `@deref` as a level-1 postfix operation, `place(ref | uniq, T)` only in result position, `associate Name(params)` with `wellformed` clauses, `T.(C).LentItem(a)` applications, Contract Type parameters and `for var` slots; write them back through the formatter and serialization without reinterpretation.

| Area | Required verification |
| --- | --- |
| Acquisition | Rejection of bare Non-Copy and Copy-unproven Places; `@move` and Take; transfer of temporaries and of owned decomposition; a typed borrow selects the same slot as the shorthand and rejects a non-matching Type; `@owner`/`@obj` are not transfers |
| Common adaptation and inference | Value positions and arm/`yield`/`exit`/single-item sources with and without an expected Type; generic Reborrow and the inference order; temporary borrows used inside the full expression versus stored or returned; by-value preference, selection differences after a Copy change and between generic and concrete calls; no reselection after a Loan failure; nested reference layers at a fixed `ref/U` (inner `ref` Copied with its own Origin, `uniq` layers below shared-Reborrowed, Origins met); result sources that differ only in reference layers over one Scalar unify to it without annotation (§14.9.1) |
| Selection and comparison | Multi-layer Scalar reads and the unchanged update target; multi-layer string and Tuple comparison and Literal Patterns; Loans during left and right evaluation; Contract comparison of `ref/T`; Sealed, `let obj`/`let objuniq`, payload protection, the shared upper bound; `@deref` grouping and single evaluation |
| Indexing | Non-consumed Non-Copy keys; explicit `key@ref` for reference keys; Slice `source`; several `Key` conformances; fixed-array Partial Move; `Array<uniq/Node>` implicit Reborrow selecting `indexUniq`; rejection through shared paths and getter boundaries |
| Updates | Right-hand-side-first simple and compound assignment; self-Move restoration; old-state dependencies; accessor boundaries; no safe borrow of a possibly Uninitialized target |
| Partial Move | Restoration on every path including `return`, `try`, `continue`, `exit`, `yield` and `defer`; inner destructors; Abort and double destruction; incomplete construction versus a Partial Move after completion; base and part cleanup order |
| Place results | Source and internal Origins; several returns; Never; Unit fall-through; temporary receivers; cleanup; Callable; adaptation of known value/Place results; ambiguous Scalar result overloads |
| Associated Types and matching | Origin-parameter scope and count; `LentItem(a)` versus `{name}`; formation conditions from a fixed right-hand side; definition of unbound Origins and remaining obligations; rejection of rebinding, domain narrowing and unapplied families; Contract implementation identity separate from Function Type contravariance |
| Patterns | Multi-layer Tuple, enum, Unit and Literal Patterns; no selection for a single name; the shared upper bound; guard `ref/T` and the escape ban; false guards and Wildcards; `positiveOrZero` at a fixed `i32` result; a `var` binding on a shared or exclusive path rejects a referent-Type assignment with a diagnostic that names the mode and suggests `@deref`, `E@owner` or `E@move` |
| Iteration | Bare `ref` Subjects; temporary, `x@owner` and `x@move` Subjects selecting `IntoIterable`; a bare `uniq` Place selecting `UniqIterable`; rejection of `@uniq` on a `let` slot; `for var`; item Types independent of the mode; early exit and resumption through `UniqIterable` of standard iterators and adapters; remainder cleanup of `IntoIterable`; resumable general Iterators and Cursors versus exhausted standard iterators and adapters |
| Independence and region splitting | LendingIterator items that borrow the Iterator conflicting with the next `next`; rejection of `Self is Iterator` when `Item` would depend on `step` or the Iterator's Storage; adapters conforming to `Iterator` exactly when their iterator does; generic `nextPair` and borrowing `collect`; rejected conformance for conflicting `next` effects and specializations; destruction effects and dependencies added after return; Ref/Uniq/Owned remainders with empty, zero-sized, ordered, key-protecting and post-return cases; rejection of internal Types and forged capabilities outside Kimi; effect bounds across separate compilation |
| Generic paths | Stopping at an undetermined `T` or associated Type; following a layer proven by a public equality; no additional selection after instantiation; NaN semantics unchanged for an Equatable comparison instantiated at `ref/f32` |
| Representation and performance | Reference-equivalent ABI of Place results; nonnull Option `Some`/`None`, zero-sized referents, nesting and cleanup; Scalar reference observation and attributes; direct delivery of large items and Tuple remainders; fixed-array storage reuse versus reinitialization and escape; nonempty checks of shared traversal; sparse Dictionary amortized and full-traversal bounds |

Diagnostics name the requested operation, the selected path and the failing condition, distinguishing Type mismatch, missing adaptation, missing capability, Uninitialized or incomplete state, Loan conflict and Origin failure; a suggested rewrite is offered only when it is valid.

Performance work implements Iterators, Dictionary and adapters in Kimigayo where possible, keeping only the low-level operations of §22.1.2.5 in the built-in boundary. Measure Scalar arrays, dense and sparse Dictionaries, lending iterators and large owned items and fixed arrays separately, comparing `for` with an equivalent direct loop at O0 and O2 for results, cleanup counts, nonempty checks, redundant bounds checks, initial fixed-array transfers, calls, intermediate storage, time and allocations. Record measured figures; documentation alone establishes no improvement.
