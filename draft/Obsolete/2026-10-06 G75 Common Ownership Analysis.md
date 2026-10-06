# G75: Common Ownership Analysis

Date: 2026-10-06. Status: implementation plan; proposed support is not integrated, implemented or verified.
Planning baseline: `dev` at `1f4fe73e14f32ea2208729a9c684a652d995cdac`.

## 1. Objective and constraints

Resolve G75 by giving ordinary and Semantics-generic code one ownership model and one set of transfer and conflict rules. Bare `s/T` capture entries are the final application of that model, not a separate borrowing feature. Acceptance of a generic definition must establish legality for every admitted case, including later uses, storage, calls and destruction, before any concrete use exists.

The user selected this direction on 2026-10-06 and requires a fresh implementation. Do not merge, cherry-pick, copy or port implementation code or test code from `held/n11-conditional-capture` (`fc5ab7cd`). Its historical failure categories may inform coverage, but write reproducers and expected public records independently from the formal specification. Build on the current dev infrastructure; “fresh” does not mean rewriting unrelated compiler facilities. Reassess dev's conditional-Reborrow shortcuts too: they are not a correctness reference.

This replaces the operation-whitelist approach in [PLAN G75](../../docs/dev/PLAN.md). An unsupported language feature remains a located implementation limit, but a new list of allowed surface expressions is neither the architecture nor the completion criterion. Do not claim G75 complete by converting its required valid forms into Unsupported diagnostics.

This request produces a plan only. It does not change executable support or integrate specification text. The existing G75 soundness gap remains open until the implementation units below pass.

## 2. Problem and evidence

The definition of a pair-layer acquisition can differ by Semantics. With `s is owner or uniq` and `T is Copy`, `let local = v` Copies in the owner case and creates an exclusive child Loan in the uniq case. A later write through `v` must conflict when `local` is subsequently used in the uniq case. The currently recorded generic form accepts that write although the concrete `uniq/i32` form rejects it. A store through a generic out-parameter can lose the same dependency. Separately, valid bare conditional capture entries are rejected as `TransferRequired_Kd`.

The earlier attempt repeatedly repaired propagation through individual routes: source writes, returned call arguments, output storage, references in Fields and Tuple parts, Origin-slot Fields, and component Moves. It also introduced false conflicts for unrelated arguments, chained stores, and iteration/pattern bindings. These are evidence that both preservation and precision are required; they are not evidence that a shorter allowed-syntax list is sufficient.

Relevant current implementation:

| Entry | Observation / intended treatment |
| --- | --- |
| `Binding.ConstraintValidation.HasConditionalReborrowPlan` | Decides whether admitted acquisitions are legal. Retain the finite proof obligation, but represent its cases explicitly. |
| `OwnershipAnalysis.Use` | Emits a definition-only exclusive Borrow and registers `ConditionalReborrows`. Replace this separate route with case-resolved ordinary acquisition. |
| `OwnershipAnalysis.Instances.FollowsReference`, `SelectedPlace`, `Concrete` | Definition and instance handling of pair layers differ. Replace the distinction with one analysis context that can carry abstract Types and fixed Semantics. |
| `OwnershipBody.Borrows.VerifyBorrows`, `AddExplicitDependencies`, `RetainBorrowAuthority`, `ResultArgument` | Type-Origin dependencies, extra generic propagation, retained authority and ancestry recovery meet here. Replace reconstruction for migrated operations with explicit common facts; preserve requirement-result and reservation semantics. |
| `OwnershipBody.StoredBorrows`, `OwnershipBody.Solve` | Existing stored-Loan activity and converged CFG state are foundations to reconcile, not a second lifetime authority beside the new model. |
| `OwnershipModel`, `OwnershipValue`, projections, Move Paths, construction/decomposition plans | Existing logical operations and identities can carry the new facts. Do not introduce a parallel executable IR merely to fix G75. |
| `Binding.Closures`, `OwnershipAnalysis.Closures`, `BodyLowering.Closures` | Capture initialization, receiver requirements and lowering must consume the same acquisition plan. |

The navigation authority is [CODEMAP](../../docs/dev/CODEMAP.md). Product limitations remain in [STATUS](../../docs/STATUS.md). Historical reviews under `artifacts/verify/20261005-n11-held/` are ignored local evidence and are not an input dependency of the implementation or tests.

## 3. Language contract and specification work

Keep the current language rules: SPEC §3.5 and §7.6.2 define acquisition; §7.6.3 requires per-case Closure receivers; §8.7 bounds proof; §8.9–8.10 require universal definition checking; §15.3–15.6 preserve actual Loan identity and distinguish it from Origin compatibility; §16 governs cleanup. `SETTLED.md` was consulted; no settled non-change needs reopening for this design.

The following are design constraints, not new source syntax:

1. An Origin is a lifetime contract, not the identity of a reference or a grant of access. Equal Origins, equal Types and equal Semantics never by themselves identify two Loans.
2. Copy, Move, Reborrow, storage, projection and cleanup retain exactly the dependencies required by their semantics. Copy can retain dependencies; Move does not extend any referent's lifetime.
3. A Reborrow has a parent capability and a referent anchor. It does not acquire a dependency on the parent variable's storage unless the access path actually borrows that storage (§15.6.3).
4. Definition checking preserves each admitted case. No strongest-case receiver or fabricated exclusive Loan is assigned to a Copy case. Semantics cases and runtime CFG alternatives are different dimensions.
5. Calls use validated public contracts. No private-body inspection, favorable concrete instantiation, hidden Constraint, new overload selection or unverified effect assumption repairs a definition.

U0 audits whether the existing prose fully determines the call/store and reassignment expectations. The intended targets of any necessary clarification are:

| Target | Required clarification, if absent or ambiguous |
| --- | --- |
| SPEC §8.9–8.10 | Substitution preserves the verified per-case acquisition/dependency plan, including definitions without uses and nested bodies. Keep proof failures distinct from compiler limits. |
| SPEC §15.4.4, §15.6.1–3 | Distinguish storage from its reaching contents; retain child ancestry through transfers and part extraction; relate reassigned locals without extending a dead value's Loan or killing a live descendant. |
| SPEC §15.3.7, §15.6.4 | Describe input/result and writable-storage dependency channels from the complete public contract, including abstract Type contents and the difference between a borrowed slot and its stored value. |
| SPEC §7.6.2–3, §15.8 | Capture initialization and receiver requirements use the ordinary, case-indexed access rules. Existing rules may need only examples. |
| SPEC §23.3.6 | Explain a generic failure by its admitted case and actual Loan evidence within the existing record model. Amend the schema only if an observable fact cannot be represented. |
| IMPL §21.3.1, §21.3.4, §21.4 | Describe verified semantic plans, substitution validation and invalidation; keep internal data structures out of the language rules. |

Do not revise a rule solely to make an existing implementation pass. If a real ambiguity or inconsistency requires a semantic decision, settle it in U0 before the dependent unit: record the old/new behavior, rationale under the Principles, positive and negative examples, and affected tests/programs. Integrate normative text into the formal chapters and update the SPEC index as needed; the formal spec must never depend on this file. Record every intake in `draft/INTEGRATED.md` with section dispositions and freeze only the integrated scope. No intake is claimed by this planning commit.

## 4. Common semantic model

Names below describe concepts; they are not commitments to new public C# types.

### 4.1 Identities and state

| Concept | Meaning and invariant |
| --- | --- |
| Place path | A storage root plus a logical path: Field identity, Tuple position, constant fixed-array index, Case payload or environment slot. Dereferencing selects a referent through a capability; it is not a field-like step that erases the distinction. |
| Value definition | The contents produced at an operation, with reaching definitions at each use. A write gives a Place new contents; it does not rename the Place. Reuse existing defining-operation IDs where possible. |
| Loan identity | Creation site/context, borrowed path or anchor, mode, parent capability and lifetime obligations. Two loans remain distinct even when their Origins are equal. |
| Carried dependencies | Loans and abstract dependency channels held by a value, indexed by logical component. An opaque `T` retains its contract-derived channels even when its Fields are unknown. |
| Case context | Binding-identified Semantics choices plus declared Type/Origin/length premises. It never replaces unknown `T` with a sample concrete Type. |
| Operation evidence | Source span, selected semantic operation and the dependency/authority steps used by diagnostics and verification. It survives normalization and substitution. |

For unknown aggregate shape, use a finite symbolic contents channel tied to the formal binding and its complete contract. Do not enumerate possible user Types or assume unknown contents are Owned, Copy or empty. Known Fields and payloads refine paths; opaque contents remain conservative according to their published contract. Type-identity premises may relate channels, but post-substitution Type equality alone is not a reason to attach every same-typed argument's Loans.

Distinguish three relations explicitly: where a value is stored, which referent it authorizes access to, and which parent Loans it depends on. A borrow of `slot` and the borrowed value stored inside `slot` must never become interchangeable.

### 4.2 Transfer rules

Every ownership-relevant existing operation and value kind must map to a defined rule, or explicitly have no ownership effect. An unclassified internal operation invalidates verification with a structured invariant diagnostic; it is never interpreted as an empty dependency set. This is exhaustive coverage of compiler operations, not a surface-syntax whitelist.

| Operation | Shared rule |
| --- | --- |
| Copy | Create a value carrying the dependencies of the copied contents; preserve the source. Require Copy evidence for this case. |
| Move | Transfer contents and component dependencies; invalidate the taken source path. Preserve Loan IDs/ancestry without creating a new Loan or erasing one retained elsewhere. |
| Borrow / Reborrow | Check the selected path and authority, create the ordinary Loan/child relation, and attach it to the result. Suspension applies to conflicting access through ancestors, not to legal access through the child. |
| Project / load | Select the component or referent and its actual dependency channel. Reading a stored reference, borrowing its slot and following its referent are distinct operations. |
| Construct / decompose | Map component paths into or out of a containing value. A closure environment uses this same rule. Taking one component does not transfer unrelated sibling dependencies to it. |
| Store / replace | Check the destination path and destruction of its old contents, then install the incoming value's dependencies. Exact destinations allow a strong update; unresolved overlapping destinations require a sound may-update. A may-update never kills all possible old contents. |
| Call | Instantiate the chosen public contract's result, storage and effect channels with prepared argument values. Preserve reservations and activation. Do not infer flow from matching post-substitution Types. |
| Join / loop | Join reachable alternatives while retaining path/definition/Loan correspondence; solve the finite CFG to convergence. Do not combine mutually exclusive predecessor facts into a fictitious simultaneous conflict. |
| Drop / scope exit | Include observable destruction uses, test invalidation of anchors, then end the destroyed value's holdings. Other holders and live descendants keep their obligations. Preserve existing cleanup order. |

A root's replacement checks all overlapping live parts; a part's replacement changes only that part and affected overlaps. Dynamic collection elements use the existing overlap contract, including proven disjoint splits. An unknown index does not acquire constant-index disjointness or Take permission.

### 4.3 Flow and local regions

Use the existing CFG as the execution-order authority. Maintain reaching contents for mutable Places and component paths, together with forward dependency transport and backward required-use liveness. Allocate abstract definitions by syntax/operation and context, not by runtime loop iteration. Loops converge over that finite domain; a Loan site reused on a back edge cannot erase a retained Loan from a previous iteration.

Joins retain which content and dependency facts arrive together. A practical initial representation is predecessor-indexed value alternatives with shared immutable dependency sets and common predecessor identities across the affected Places. Independent per-variable unions must not invent a combination that no predecessor contains. This does not require general runtime predicate solving. Validate the alternative/overlap transfer rules on diamonds, loops and overwrites before claiming precision on reassignment.

The finite representation must also distinguish a creation site from one execution's Loan. When a back edge may keep earlier instances of that site alive, summarize their possible coexistence; do not infer that two values carry the same capability merely because their creation-site ID matches. An overwrite may kill only the holdings it actually replaces. Parent-child evidence comes from value flow, and an unresolved loop summary grants no disjointness or ancestry exemption. U2 includes a retained child across a repeated creation/store site as a mandatory soundness case.

Infer local regions from these same facts for the assignments needed by G75. Do not treat the union of all values ever assigned to a variable as its current content. A store must not affect earlier program points; killing one holding must not end a Loan still needed by a moved child, an output store, a result or a destructor. Honor required checking of unreachable code and existing continuation rules separately from executable reachability.

This work includes the G56 local-region edges necessary for G75's carriers, reassignment and joins. It does not close unrelated G56 contract/applicability/contravariant-slot limits. Record the exact implemented intersection in PLAN and STATUS when verified.

### 4.4 Function boundaries

Derive dependency channels from formal binders, complete parameter/result Types, Origin relations, storage authority and published effect bounds, before incidental equalities of a particular substitution can hide their roles. Instantiate that mapping at each call. A result with several permitted sources retains every permitted source; do not select one by reading the callee's private return statement.

For `second<A, B>(a: A, b: B) -> B`, preserve the distinction of the formal channels and apply the public contract's actual relations. Binding both target Types to the same scalar or pair target must not automatically make the result carry `a`'s Loans. Conversely, if the public contract permits several input sources, the caller must retain them even when one known body always returns `b`. U0 must check this against separate compilation and the inherited Origin contracts of explicit specializations; no body-specific shortcut is permitted.

For `put<T>(slot: uniq/T, value: T)`, the payload may be stored in `slot`'s referent. The Loan used to borrow the slot is not itself the stored payload. With two exclusive arguments, account for permitted transfers of their contents without inventing reciprocal Loans of the argument variables. Calls preserve possible old contents unless a public guarantee establishes complete replacement. A no-op body does not strengthen a broad contract at its callers.

Apply the same contract instantiation to direct calls, Function Items, callable values, Contract requirements, result projections and source-library collection operations. Intrinsics supply validated equivalent contracts. Unknown effects remain conservative under the SPEC; they must not become no effects merely because G75 needs precision.

## 5. Finite Semantics cases and phase boundaries

### 5.1 Definition checking

1. Resolve source and select operations under the declaration's existing premises and binding deadlines. Publish symbolic acquisition/access plans with the bound expressions.
2. Identify independent Semantics bindings actually used by ownership. Preserve the identity of repeated and nested pair layers. Uses of the same binding always share one choice; proven binding equalities share it too.
3. Form finite cases from the admitted sets under §8.7. Remove a case only when the existing proof rules establish that it is impossible. Do not prune based on observed call sites or private bodies. Do not add Copy evidence by choosing an owner case.
4. Resolve each operation's outer Semantics in a case context; keep Types, lengths, Origins and abstract contents symbolic. For example, owner chooses ordinary Copy only with its required proof; uniq chooses the ordinary Reborrow of an abstract `T`.
5. Feed each case to the same operation normalization and ownership solver as a nongeneric body. Check later storage, uses, receiver permissions, results and cleanup. Acceptance requires all admitted cases.
6. Retain the verified case plan and its source/premise identity. A concrete substitution selects and specializes that plan. Generation checks correspondence with the verified effects; it cannot first discover a new semantic body obligation.

Singleton concrete Semantics is the one-case path through this machinery. General `T` acquisition and abstract dependency transport still use declared capabilities; finite Semantics splitting is not a replacement for universal Type/Origin reasoning. A nested pair `s/(t/T)` keeps the source layer identity: owner removes that layer, even if `t/T` is itself a reference.

Case partitioning alone cannot repair wrong store or extraction rules. Sections 4.2–4.4 are required before routing G75 through the case engine. Existing concrete behavior is a comparison oracle only where independently justified by SPEC.

### 5.2 Binding, captures and lowering

Binding owns selected declarations, canonical contracts and symbolic access intent. A shared semantic access-plan builder makes case-indexed acquisition and environment-access facts available before receiver-sensitive checks need them. Receiver classification folds those same facts for each case, including all selected-source paths and nested captures. It must not retain an independent syntax walk that approximates the acquisitions differently from ownership.

Ownership materializes logical paths, content flow and Loans from the committed access plans. It cannot change overload selection, infer new Constraints or repair a failed Binding proof. Cyclic or unavailable prerequisites follow the existing bounded completion/diagnostic protocol; a provisional receiver requirement is not an acceptance proof.

Explicit captures initialize ordinary environment slots left to right. A capture has no conditional-borrow propagation table. Environment initialization, nested captures, component Moves and receiver-dependent results use the common rules. Preserve omitted-list Copy requirements, capture order, `let`/`var` permission, consuming cleanup and Ownness requirements of erasure. Every call, Callable fit and erasure is checked for every admitted case; the instance uses its actual minimum receiver.

Lowering consumes verified operation and cleanup plans and resolves physical representation. It must not reclassify acquisitions or reconstruct capture Loans. Cache keys and invalidation include source snapshot, bound declarations/contracts, case choices, premises and relevant configuration. Generation keys that erase Origins do not erase proof identity.

### 5.3 Resource and performance policy

Start with explicit, deterministic enumeration of the relevant finite Semantics combinations and one reusable solver workspace. Share immutable syntax, contracts and component paths. Reuse scratch arrays and intern repeated dependency sets; do not allocate a mutable graph per Loan or retain a full Place-by-Place matrix per case by default. Do not introduce a general SAT/SMT solver.

The product of independent admitted sets can grow exponentially. Calculate case and work bounds with checked arithmetic before expansion, and charge visited states, alternatives, paths and transfer work to documented compiler budgets. Exhaustion is an explicit Resource diagnostic, never a Language error, skipped case, successful verification or strongest-case fallback. Reuse `OwnershipStorageLimit_Kd` for its actual storage condition; if a work/case budget needs another public record, define it through the diagnostic workflow in U0/U1.

After correctness, share equivalent subplans only when their effects, dependency channels and continuation state agree. Performance measurements must include increasing numbers of independent bindings, nested aggregate paths and loops, plus normal one-case workloads. Preserve existing zero-allocation warm checks and measure first-use allocation, retained capacity and scaling separately. No speedup is claimed by this plan.

## 6. Implementation units and delivery order

Each row is a coherent deliverable with independently specified expectations, implementation, focused verification and a descriptive commit. Split a large row at a proven invariant boundary if needed; do not publish a half-migrated acceptance path. U0/U1 may add non-accepting infrastructure. During migration exactly one complete solver authorizes a body; a shadow comparison never overrides a failure or silently falls back. Published support remains unchanged until a unit establishes it.

| Unit | Work | Completion gate |
| --- | --- | --- |
| U0 — Contracts and fresh reproducers | Inventory existing operation/value kinds and producers/consumers; specify the transfer table and call/store channels; author all failure/valid pairs in §7 from SPEC. Record the baseline's observed gaps and allocation/timing workloads. Settle the spec clarifications of §3, if needed, and record intake. | Every G75 family has an expected outcome independent of either old implementation. Call bounds, Origin equality, specializations and case correlation have unambiguous examples. No known failing behavior is blessed by a regression expectation; baseline failures remain evidence until their repair unit. |
| U1 — Shared access and identity model | Introduce the shared access plans, logical paths, content definitions, Loan identity and abstract channels within the existing semantic/ownership model. Map every current operation and value kind to a rule, including explicit no-effect cases. Add resource accounting and source evidence. | Concrete one-case operations can be represented without losing source/evaluation order. Omitted rule coverage is an invariant failure. Declaration, storage, referent and Loan identities remain distinct; pooled state resets after failures. |
| U2 — Common flow, storage and projections | Implement the common transfer/region engine for Copy/Move/Borrow/Reborrow, aggregate construction/extraction, exact and may stores, reassignments, joins, loops and destruction. Reconcile current stored-borrow activity and reservations with the same facts. | Concrete and abstract-content tests show preserved child ancestry, sibling precision, correct overwrite behavior and no future-to-past stores. Existing concrete ownership, continuation, cleanup and allocation regressions pass. |
| U3 — Common call contracts | Implement contract-derived result/storage/effect channel substitution; route direct, requirement and callable boundaries through it. Cover out-parameters reached through Fields/Tuples/Origin-slot structures, multiple arguments and nested results. | `second` does not inherit incidental inputs; permitted multi-source results stay conservative; `put` retains payload Loans without retaining the slot borrow as payload. Separate bodies with the same contract yield the same caller obligations. |
| U4 — Semantics case verification | Add the finite case context and normalization shared with the concrete path. Retain abstract `T` contents and universal Origins. Route generic local acquisition, `@follow`, storage, calls and cleanup through U1–U3; replace dev's definition-only conditional Loan propagation for these paths. | The current local-alias and output-store soundness probes reject at definition, even without callers. Valid owner/uniq/ref cases pass. Multiple/nested bindings remain correlated. Case-resource exhaustion is bounded and never silently accepted. |
| U5 — Ordinary capture initialization | Implement bare conditional entries from scratch using environment slots and the same access plans. Derive receiver requirements per case; connect nested captures, expression/block bodies, Callable checks, erasure and receiver-dependent results. | Bare valid entries work, invalid exclusive calls fail with the right case, and every U2/U3 route works for captured carriers without a new carrier-specific propagation mechanism. No acquisition is inferred at an omitted-list boundary. |
| U6 — Substitution and removal of duplicate authorities | Connect closed generation plans, cleanup and ABI to the verified case plan; cover direct calls, Function Items, closure instances and source-library operations. Remove obsolete conditional-Reborrow state and its propagation/recovery branches; migrate shared requirement-result consumers before deleting shared helpers. | No `ConditionalReborrows`-style second authority remains. A checked supported body cannot lose a dependency or change a receiver at generation. O0/O2 observations and destruction order agree with the plan. Rebinding invalidates every affected certificate. |
| U7 — Closure of G75 | Complete the targeted generated sweep, diagnostic publication review, allocation/reuse and fixed-workload measurements; run Session and original milestone harnesses. Update support and navigation documents from evidence. | Every criterion in §9 is met. Failed and successful runs are retained and tied to the committed inputs. G75 closes only here, not when the first capture test passes. |

Serial critical path: U0 → U1 → U2 → U3 → U4 → U5 → U6 → U7. This is a multi-unit compiler refactor, not a small capture patch. The plan authorizes no concurrent writers or subagents. A later implementation session should preserve the repository's current track scheduling unless the user selects G75 as that session's target.

Keep an architecture checkpoint after U0 and after U3: for each basic operation, identify the fact it consumes, the fact it produces and why a legal concrete substitution preserves that step. If an opaque Type's channels, a may-store or a join cannot be justified, repair the model and its contract before adding capture routes. This checkpoint is design work within the authorized implementation, not a request for routine user approval. Do not commit failing regression tests as successful implementation units; retain initial failing probes as evidence and add each permanent assertion with its repair.

G75 does not depend on adopting `held/p26-applicability-chain` either. Preserve the currently selected Binding contracts and record genuine G76 blockers instead of importing unrelated code. G77's nonterminating Binding of a bare-entry closure in a name-binding match arm can block the corresponding U5 tests: repair that prerequisite as a separate bounded Binding unit before claiming the affected family, rather than timing it out and counting it as an ownership rejection. Shared-object U6/G70 may benefit from content identities, but its handle-specific support remains a separate unit and completion claim.

## 7. Verification design

### 7.1 Required semantic families

Each family needs a failing program and a nearby valid counterpart. A negative case keeps the child live across the conflict; the positive ends the relevant use first or uses a proven independent path. Avoid expectations inferred solely from “the old compiler accepted it.”

| Family | Required distinctions |
| --- | --- |
| Local acquisition | owner Copy versus uniq Reborrow, source read/write/second Reborrow, explicit Move and use-after-Move, no-use generic definition, owner without Copy evidence |
| Passing and returning | Identity/first/second functions; multiple same-target inputs; distinct, equal and shortened Origins; results with one and several permitted channels; nested result aggregates |
| Stores | Direct local assignment, `put`, reference store, Field, Tuple, fixed-array part, Origin-slot Field, setter closure, chained stores, and a store before/after the source is filled |
| Component acquisition | Whole and part Moves through Tuple, fixed array, struct, Option/enum; an extra aggregate or call around the extracted value; unrelated sibling Loans stay separate |
| Calls with multiple holders | Two `uniq/T` arguments, no-op versus content-transfer implementations under the same contract, reservations, later-argument inspection, result borrow, abandoned call cleanup |
| Control flow | `if`, `match`, `do`, pattern payloads, owning/shared `for`, removal results, branch overwrite, back-edge retention, break/return/defer and required unreachable checking |
| Closures | Bare and explicit captures, `var` entries, nested captures, component transfer, calls before/after source access, Shared/Exclusive/Consuming requirements, Function Items and permitted erasure |
| Cases and abstract Types | Repeated one binding, two independent bindings and all admitted combinations; owner/ref/uniq and objref/objuniq/raw where the contract and operation admit them; nested pair layers, reference-containing Copy `T`, Non-Copy move-only carriers, abstract associated Types and symbolic lengths |
| Storage boundaries | Source Array/Dictionary/Slice operations already supported, stored references versus slot borrows, dynamic-index overlap, disjoint pair identities, static/raw anchors without inventing safe authority |
| Lifetime and cleanup | Last use, observable `drop`, overwritten holder, surviving transferred child, loop reuse, empty/zero-sized payloads, receiver-dependent and per-call input-dependent results |
| Publication and reuse | CLI text/JSON, LSP, record limits, independent errors, edit/rebind invalidation, failed analysis followed by a valid request, memory/case limits |

Do not use a dynamic Array component Move where SPEC permits Take only on static Move Paths. Keep independent existing Unsupported forms labeled with their actual PLAN owner; a test blocked by one is not evidence that the G75 operation works. Required generic storage routes must be exercised through supported counterparts and any remaining G75 blocker must be repaired before closure.

### 7.2 Oracles and structural obligations

Use three kinds of evidence together:

1. Independently written SPEC expectations for legality, output, destruction and public records, including unused definitions.
2. Normalized operation/plan checks: every transfer preserves the required Loan identity and ancestry; stores retain value/slot distinctions; cases cover the admitted set; substitution preserves effects and cleanup. Compare logical facts, not incidental numeric IDs or emitted pointer types.
3. A deterministic generated family of small programs, substituting representative Semantics and wrapping an acquisition in storage, projection, calls and control flow. Compare generic and concrete supported forms against the independent expectations. Concrete behavior and finite samples are not a proof over all `T`/Origins.

The sweep composes the risky routes (for example, construct → Move component → return through call → store through a Field reference) instead of testing each syntax once in isolation. Keep its seed/input list, expected classification and minimized failures. Select meaningful pairwise and deeper compositions; do not create an indiscriminate cross-product of every language feature.

A generic plan's proof obligation is compositional: for every admitted case, each normalized operation obeys its transfer contract, CFG joins preserve the required alternatives, and substitution preserves those facts under the public premises. U0–U7 must review these obligations explicitly. Passing generated examples supplements that argument; it does not replace it.

### 7.3 Diagnostics and execution

Follow DIAGNOSTICS §10 for every changed check. Locate the actual conflicting access, name the admitted Semantics case when relevant, and retain the child creation, holder and necessary transfer/store evidence using existing related-location roles. Distinguish a real violation, missing proof, unsupported independent feature, resource exhaustion and internal invariant failure. Do not claim that abstract `T` is Non-Copy when the rejected operation depends on `s = uniq`.

Diagnose after converged analysis. Combine identical case failures only when their facts and identity agree; retain independent causes and deterministic ordering. Inspect representative CLI text/JSON and LSP output and verify factual wording under output limits. Offer a repair only when its preconditions have been established; no automatic `@move` suggestion that merely changes which unsound route is taken.

Proposed new test groups: `OwnershipTransferTest`, `OwnershipCallDependencyTest`, `GenericOwnershipCaseTest`, `GenericOwnershipFlowTest` and `ConditionalCaptureTest` (names to confirm in U0). Reuse existing test helpers, not held-branch tests. Existing regression starting points include `BorrowAcquisitionTest`, `PairFollowTest`, `OwnedStoredReferenceTest`, `StoredReborrowAncestryTest`, `DictionaryStoredReferenceTest`, `CallReservationTest`, `ClosureReceiverClassificationTest`, `GenericCaptureStorageTest`, `ClosureResultOriginTest`, `InputDependentValueCallTest`, `BorrowDependencyStorageTest`, `OwnershipStorageLimitTest` and the continuation tests; confirm the effective method selections when verifying each unit.

For each implementation unit, use incremental selected-method runs only for feedback, then `scripts/verify.ps1 -Class <actual related classes> -Fixtures <fixtures regenerated by those classes>`. Include relevant functional and `Purpose=Allocation` tests. Native positive fixtures execute at O0/O2. At session end run `-Mode Session` once, with the affected native families and required completed-program harnesses; close G75 with Programs 26 and 39 and the collection/borrowing integration harnesses, plus the earlier completed-program set required by PLAN. No NativeAOT.

Run explicit Benchmark measurements for the common hot path and case scaling with fixed inputs and warm-up conditions; Benchmark/Playground edits require the Session whole-solution build. Never edit sources during verification. Keep failures in `artifacts/verify/`, measurements in `artifacts/benchmarks/`, and associate successful evidence with each commit using `verify-commit.ps1` before pushing. Verification identity includes exact source/configuration and its stated toolchain-check status.

## 8. Documentation and migration responsibilities

| Document / artifact | Update point |
| --- | --- |
| This proposal, PLAN G75 | Record the selected plan now; record unit progress without claiming implementation. Keep PLAN under 200 lines. |
| SPEC/IMPL chapters, SPEC index, INTEGRATED register | U0 or the unit that deliberately changes/clarifies a normative rule; self-contained text, examples and disposition in the same commit. |
| CODEMAP | Each unit that introduces/moves an entry point or changes phase responsibility; navigation only. |
| DIAGNOSTICS | When public records or internal fact ownership change; include reviewed output and remaining uncertainty. |
| STATUS | Only after verification changes an executable or diagnostic support boundary. Until then retain the dev soundness gap and bare-entry rejection. |
| PLAN_HISTORY | A short record per implementation/planning session; detailed proof and measurements belong in the commit/evidence. |
| Milestone sources/README, STYLE, LIBRARY | Only if a deliberate spec/API change affects them; no source rewrite to evade a Loan check and no automatic library/API change. |

Remove obsolete code only after all of its consumers use the common model, including requirement results, reservations, stored authority, diagnostics and cleanup. There must be no accepted-body path where the old and new mechanisms can disagree about the same Loan. During an incomplete migration, retain existing support or report a precise implementation limit at an actual unsupported boundary; never accept a body because either of two checkers happens to pass it.

## 9. Completion criteria and Principles

G75 is complete only when:

1. Its current generic local and output-store soundness gaps are rejected at definition, including unused definitions, with the correct conflict evidence.
2. Bare conditional capture entries, their per-case receivers and every required transport family in §7 work through the common rules. Valid last-use, independent-holder, iteration/pattern and chained-store counterparts are accepted.
3. All admitted Semantics cases are checked with abstract Type/Origin contents intact; no call-site enumeration, strongest-case shortcut, hidden requirement or syntax whitelist establishes acceptance.
4. Move, storage, projection, calls and cleanup preserve Loan identity/ancestry and distinguish values from slots. Source changes invalidate their evidence. No supported `check` acceptance reaches a new semantic ownership failure only at generation.
5. The conditional carrier-specific propagation authority has been removed, shared consumers have migrated, and no held-branch implementation or tests have been reused.
6. Focused checks, diagnostic inspection, allocation/reuse checks, full Session, required native/harness runs and measurements are complete, with exact commit association and explicit remaining limits outside G75. Any required unresolved G75 form keeps G75 open.

Principle 1 is served by one acquisition/Loan model shared across concrete values, generic values and captures. Principle 2 is served by finite case proof and public call contracts with explicit resource limits. Principle 3 is served by keeping Copy, Move, Reborrow, slot access and retained dependencies distinct, without new implicit conversions or hidden Constraints. Principle 4 is served by persistent source/operation evidence, accurate structured diagnostics and checks attached to exact source/configuration; this plan does not introduce an unrelated new CSP endpoint.
