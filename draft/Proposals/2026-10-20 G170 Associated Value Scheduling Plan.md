# G170 Associated Value Scheduling Plan

Status: 一部取り込み (open; implementation design, no specification integration).
Owner direction: 2026-10-20. Present solution options for G170's associated-inference cycle, from local to fundamental;
then, by the owner's later instruction, write one design from the most promising option, without competing designs or
independent review.
Investigation baseline: `8400ce722daf294ef64d89a3a07c0376a79aca3c` (`dev`).
Relation: replaces the blocked source-lifecycle part of PS1 in the
[Canonical Premise Supply and Transferable Plan](2026-10-20%20Canonical%20Premise%20Supply%20and%20Transferable%20Plan.md)
(below, "the premise plan"). All other gates of that plan, R4-M83's hold and R4-M79's BU/R5 pause remain.

This document is a plan. It authorizes no compiler change, SPEC change, vocabulary entry or acceptance change; §8 lists
the owner decisions the implementation needs.

## 1. Problem

### 1.1 The cycle

G170's repair (the premise plan's design D) makes premise supply a demand-driven request inside Binding's proof. Proof
normalizes Types, and normalization of a projection on a struct or enum reads an associated value that inference may
still be determining. Inference in turn needs normalization and proof: identification keys, extraction of declared
results and the completion proof that a destination's premises prove a source's premises. So the supplier must run
inside inference, where some values are excluded (SPEC §8.5.5 R1, R3) and others are not determined yet.

PS1's two lifecycle reviews blocked on exactly this: "cross-definition associated-value completion: contextual evidence
exclusion has no complete readiness transition" (`b2b32fe8`). The owner calls it G170's associated-type-inference
circular dependency. The investigation shows that the current inference is itself wrong here, independently of G170:
the supplier inherits a defect, it does not create one.

### 1.2 Evidence

Probes on the baseline with `Kimi.exe check` (sources in Appendix A). They are new observations of this investigation;
the planning session ran them, and the read-only investigators did not re-run them.

| Probe | Shape | Observed | SPEC §8.5.5 |
| --- | --- | --- | --- |
| PA/PB, both orders | SPEC's own example: PA.X ← PB.Y, PB.W ← PA.Z | Rejected in both orders; the failing value flips (Q.W when PA is first, P.X when PB is first) | Valid in every order: X=i64, Z=i32, Y=i64, W=i32 |
| excl, both orders | A.X ← B.Y ← A.Z | A.X fails in both; B fails with a different code per order | A.X excluded; B.Y = i32 |
| tri, two orders | A.X ← B.Y ← C.Y ← A.Z | Diagnostics differ by order | A.X excluded; B.Y = C.Y = i32 |
| cyc, both orders | A.X ← B.Y ← A.X | Both fail in both orders | Both undetermined |
| alt, both orders | D.X ← a: i32, D.X ← b: E.W; E.W ← D.X | **Acceptance depends on order**: E first accepts; D first rejects | Depends on the undefined word "depends" (§2) |
| g170, g170b | G170's recursive CopyTree query; with an unused CopyTree-spelled parameter | Copy Unknown; Proven with the extra parameter | Proven |
| nonrec, nonrec-hint | The same supply failure with non-recursive `Outer`/`CopyOuter` Contracts | Copy Unknown; Proven with the hint | Proven |

The first five rows break §8.3.3 Finality and IMPL A.1 (checking order never changes a result). The alt row changes
acceptance with declaration order. The last row shows G170's supply defect is not limited to recursive Contracts, so
concrete conformances can reach the same supply path.

### 1.3 Cause

The design chain (AGENTS, Design before code):

> SPEC §8.5.5 schedules associated values one by one ("may be used as soon as that value is determined, without waiting
> for the other definition's remaining values") and excludes evidence by dependency (R3) → Binding represents
> availability per nominal definition and exclusion by the host call stack: a batch in State 1 reads as null for every
> reader, its partial outcome is frozen as final, and no read is recorded → the owner is Binding's associated inference,
> at its single read point → definition batches, the collecting stack, structural "pending" classification and the
> readiness question all become unnecessary.

Current mechanism (file:line at the baseline; `AI` = `Binding.AssociatedInference.cs`, `AT` = `Binding.AssociatedTypes.cs`):

- One `AssociatedInference` batch per struct or enum, State 0→1→2 once per pass, with `collectingAssociated` as an
  implicit stack (AI.cs:9, 112-154, 229-350). "Collection is finished for the entire nominal definition before any
  inferred value is visible" (AI.cs:305).
- Every read of an inferred value passes one point: the hole branch of `ResolveAssociated(path, identity)`
  (AT.cs:516-522), reached from `ContractTypeCore`'s struct/enum receiver branch (AT.cs:665-674), inherited sources
  (AT.cs:482-506) and explicit candidates. It runs the other batch nested on the host stack and returns null while that
  batch is State 1.
- The null leaves a projection that `PendingAssociatedType` (AI.cs:457-471) detects structurally. It cannot tell an own
  value (R1, correct) from a value excluded only by the stack, a failed value or a read before readiness, yet every
  consumer treats it as final: uncompleted syntax (Binding.Types.cs:524-527), a partial header (Binding.cs:1272-1292),
  `Pending` identification → `InferenceProblem.Input` (AI.cs:272-290), extraction → `InferenceProblem.Dependency`
  (AI.cs:420-423). The nested batch stays State 2; `VerifyConformance` publishes the frozen problem as the Language
  error `AssociatedTypeInferenceFailed_Kd` (ContractMatching.cs:234-257).
- No read is recorded: `AssociatedEvidence` keeps only the normalized Type, `Dependency` names no target, and caches
  (`batch.Normalized`, `requirementMatches`, completed headers, explicit `Result`) hide reads.
- During the sweep `contractHeadersReady` and `capabilitiesReady` are false (Binding.cs:461, 483-484): inference-time
  proof is premise-only, and the `VerifyConformance` collecting guard (ContractMatching.cs:157-160) is unreachable.
- Cross-definition chains nest on the host stack, about ten frames per definition (IMPL A.4 risk).

### 1.4 Sites sharing the cause

| Site | Defect from the same cause |
| --- | --- |
| AT.cs:516-522 hole branch | Stack exclusion of other definitions (PA/PB, excl, tri) |
| AI.cs:420-423 extraction | A dropped pending source when another source exists: the conflict rule is skipped in an order-dependent way |
| AI.cs:272-290 identification | A key reading another definition's value becomes a final `Input` problem |
| AI.cs:156-161 explicit candidates | `State`/`Result` computed before readiness and read during the sweep |
| AI.cs:139-154 sweep order | Dictionary order, which may differ from declaration order after edits |
| AT.cs:665-674 → nested batches | Host-stack depth equals the cross-definition chain length |
| PS1 (premise plan §4.4) | No rule for a supplier request inside inference |

Not shared, listed separately in §9: `projectionUses` duplicates from re-bound pending headers, premises mutated inside
requests (the premise plan's PS3), and completion evidence lost to stage-blackout Unknowns.

## 2. SPEC reading

Normative rules (docs/spec/08-generics-constraints-and-contracts.md): §8.5.5 line 318, R1 "No value inferred for it
enters its frozen premises", R2 "A value inferred for another definition may be used as soon as that value is
determined", R3 "Evidence for a value of the current definition that depends, directly or through other definitions'
values, on a value inferred in the current definition supplies no evidence"; identification (320), extraction (322),
completion including "Acceptance never depends on an inference work budget" (324); §8.3.3 recursion and Finality (162,
164). IMPL A.1 (order independence, no header complete early) and A.4 (deep input never exhausts the host stack).

SPEC leaves six points open. This plan adopts the readings below and proposes their wording in Appendix B; recording any
of it in SPEC needs the owner's explicit instruction.

| Gap | Adopted reading | Reason |
| --- | --- | --- |
| g1 "determined" | A value is determined when every one of its evidence items is resolved: usable, excluded or failed | All usable evidence must agree, so an earlier answer could change |
| g2 "depends" | Reads, not usable evidence: evidence depends on every value its evaluation reads, whether the read value's own evidence is usable or excluded | By analysis, the usable-evidence reading has two self-consistent outcomes on alt and the read reading has one; V1 checks both |
| g3 identification | A key that reads another definition's value records a read like any other; exclusion makes the requirement supply no evidence; it is not "pending" | SPEC's pending is only for the definition's own missing Types |
| g4 completion and conditions | Reads made by completion proofs and conditional conditions are reads of the evidence they admit | Same rule for every read |
| g5 verification | A projection's formation and conformance obligations are separate obligations, not prerequisites of the conformance whose signature uses it (the pre-rewrite text, dropped in `72fb4b79`) | Without it PA is P and PB is Q need each other; the code already separates them (`projectionUses`, Contracts.cs:864-869) |
| g6 frozen premises | An explicit specification naming another definition's projection is normalized in the reader's context | Keeps R1 and R2 without a second waiting rule |

The plan also states one existing contract as a rule: judgments made while values are being determined use the frozen
premises and concrete identity only; nominal conformance and capability evidence is not consulted (today's stage
blackout). This is what §8.5.5 asks of identification ("under the frozen premises") and completion ("the destination's
premises prove the source's premises").

## 3. Solution options

| Option | Change | Fixes | Leaves | Size / speed | Verdict |
| --- | --- | --- | --- | --- | --- |
| S1 Hold | None; G170 stays open, R4-M83 stays | Nothing | Every defect in §1; PS1, PS3 and Transferable blocked | 0 | Rejected: the defects are order-dependent acceptance today |
| S2 Located boundary (whitelist) | A read that meets a collecting definition reports a located `Unsupported_Kd` instead of the Language error; the supplier never normalizes nominal projections during inference | The wrong diagnostic category for valid programs | SPEC's own example unsupported; alt's order-dependent acceptance (the read there is not "collecting" in one order); a true cycle mislabeled as Unsupported | ~+30 lines | Rejected as the end state: it narrows support for SPEC's own example and still breaks Finality |
| S3 Re-run frozen batches | After a batch completes, reset and re-run batches that read it while collecting, until stable | PA/PB, excl, tri in simple cases | Definition granularity; the conflict rule makes the iteration non-monotone (it can oscillate); headers, syntax, scope premises and match caches computed in the first run need invalidation; alt stays order-dependent because exclusion is still decided by the stack | +100 to +200 lines plus invalidation; repeated work | Rejected: a readiness patch on the wrong representation |
| **S4 Value scheduling (selected)** | One graph of associated values; each read gets a final verdict at read time from a strongly connected component (SCC) walk; evaluation stops and resumes instead of nesting | Every defect in §1.4; gives PS1 its lifecycle contract | The §9 items, which have other causes | About +265/-165 lines; linear in values and reads; no host recursion | Selected (§4) |
| S5 One tabled Contract solver | Proof and associated values in one memoized solver (R7's target) | Everything S4 fixes, plus R7's consolidation | Persistent memoization conflicts with the premise plan's request-local storage; replaces most of the Contract code at once | Thousands of lines | Rejected now: S4's value table is the part R7 can absorb unchanged |
| S6 SPEC change: definition cycles share nothing | Infer definitions in SCC order of a definition graph; values of one SCC never feed each other | Order independence by construction | Rejects SPEC's PA/PB example and contradicts R2 | Small code, SPEC revision | Rejected: removes a rule SPEC chose deliberately |

S4 is selected because it repairs the representation the cause lives in (Fix causes in shared structure), deletes the
batch state machine instead of adding a retry layer (Short), schedules in linear time without host recursion (Fast),
and answers PS1's question with a contract rather than a case list. Against the Principles: One Concept, One Canonical
Form (one read point, one verdict rule for every reader); Local Reasoning (an evidence item's usability follows from its
own reads and the values they reach); Explicit Semantics (the gaps of §2 become stated rules); Compiler Server Protocol
(a failure names the value it read and why it was excluded).

## 4. Design

### 4.1 Owner and contract

`Binding.AssociatedValues.cs`, a new file of the existing owner, holds the scheduler. Its entry contract:

- Responsibility: determine every inferred associated value of struct and enum conformances under SPEC §8.5.5 and
  answer every read of one.
- Inputs: registered conformance paths, bound requirements and implementations, frozen premises, explicit
  specifications.
- Outputs: per value, a complete Type or a typed problem; per read, a verdict (§4.3).
- Invariants: a read never observes a value that can still change; the result does not depend on declaration, sweep
  or demand order; no host recursion across values.
- Prohibitions: no proof by search; no conformance or capability evidence of nominal Types while values are determined;
  no result of an interrupted evaluation is published.

`Binding.AssociatedInference.cs` keeps identification and extraction; `Binding.AssociatedTypes.cs` keeps normalization
and calls the scheduler at its one read point.

### 4.2 Representation

| Element | Representation |
| --- | --- |
| Value node | The existing `AssociatedBinding` of an inference hole (root path, identity). Adds an index, a low link, a status (Unvisited, Open, Closed) and a reach span. No new node class. |
| Definition context | The existing per-definition `AssociatedInference` object, without `State` and `Active`: its paths, identification results and normalization cache, each cache entry with a read span. |
| Evidence item | The existing `AssociatedEvidence` plus a read span: (destination value, source path, source requirement). |
| Read list | One flat per-pass list of value nodes; spans are (start, count) into it. Capacity is reused, so warm binds allocate nothing. |
| Reach | Per closed SCC, a bit set over the ordinals of definitions that have inferred values, stored in one per-pass `ulong` arena; members share the span. |
| Frame | A struct for the evaluation in progress: its definition, the start of its reads, the first unvisited read (Wait) and an excluded/failed mark with the read that caused it. One field replaces `collectingAssociated`. |
| Problem | `InferenceProblem` gains `Excluded`; `Dependency` keeps the own-definition meaning. The problem detail names the read value. |

An explicit binding is not a node. Its candidates are normalized in the reader's frame (g6), so their reads are the
reader's reads. Its `Result` is cached only when the normalization made no excluded, failed, Wait or not-ready read;
this replaces the post-sweep reset at AI.cs:156-161.

### 4.3 The read rule

`ResolveAssociated(path, identity)`'s hole branch becomes one call, `ReadAssociatedValue(node)`, with this table:

| Reader | Node | Verdict | Effect |
| --- | --- | --- | --- |
| No frame, before the value stage | any | Not ready (null) | Callers keep today's pending behavior and cache nothing complete |
| No frame, after the value stage | Closed | Value, or Failed (null) | Ordinary use |
| Frame of definition D | Node of D | Own (null) | Not a read; R1 is enforced by today's structural checks on the extracted Type and the key |
| Frame of D | Unvisited | Wait (null) | The frame records the node; the evaluation is abandoned and retried after the node finishes |
| Frame of D | Open (on the SCC stack) | Excluded (null) | Records the read; lowers the reader's low link |
| Frame of D | Closed, reach contains D | Excluded (null) | Records the read |
| Frame of D | Closed, failed | Failed (null) | Records the read |
| Frame of D | Closed, otherwise | Value | Records the read |

An Open node reaches the reader: it is on the SCC stack, so it reaches a node on the walk's path, which reaches the
reader. So an Open verdict and a later Closed verdict for the same node and definition always agree, and every verdict
is final when it is given. This is why no readiness transition exists: a reader sees either a finished value it may use
or a value it may never use.

### 4.4 Evaluation

The driver walks values with an explicit stack (Tarjan's SCC algorithm), starting from each inference hole in
registration order:

1. Push the node: assign index and low link, mark it Open, push it on the SCC stack.
2. Take its next unevaluated evidence item. Evaluate it in a frame of the node's definition:
   - identification of the source requirement on the source path, once per (path, requirement), cached with its reads;
   - normalization of the requirement's required and declared result on the source path, once, cached with its reads
     (SPEC: "The normalized results are traversed"), then extraction for the node's identity;
   - completion: the proof that the destination's premises prove the source path's premises.
   If the frame recorded a Wait on node v, discard the attempt (no result that saw the Wait is cached) and push v.
   Otherwise record the item's verdict: Excluded if any read was Excluded or the extracted Type keeps an own value
   (R3 "directly", today's structural check); else Failed if any read was Failed; else usable with its Type. Lower the
   node's low link by every read of an Open node, and keep closed reads for the reach union.
3. When no item remains, determine the value: a failed dependency if any item is Failed (so a source that cannot be
   evaluated never lets another source bypass the conflict rule); else the one complete Type of the usable items, or a
   conflict if they differ; else missing, with the strongest item problem.
4. Pop the node. If its low link equals its index, close its SCC: reach = the SCC's definitions ∪ the reach of every
   closed node its members read; mark the members Closed. Otherwise pass the low link to the parent.

A retry re-runs only the abandoned item; reads made before the Wait get the same verdicts on the retry, because the
nodes they read were Closed or Open and stay so until the current SCC closes. Each item is retried at most once per
distinct unvisited read, so the work is linear in items and reads. The walk's stack is heap storage; host recursion is
bounded by one item's normalization and proof.

Reads past an unavailable value stop by themselves: a nested projection on an Excluded or Failed value stays
unresolved and cannot be read further. Every other read of the item counts, also after an Excluded read.

### 4.5 Values, problems and diagnostics

| Outcome | Problem | Reported reason (adapter text, AI.cs:619-627) |
| --- | --- | --- |
| An item read a failed value | `Dependency` with the read value as detail | The declared result depends on *read value*, which is undetermined; related: that value |
| Usable items disagree | `Conflict` | Today's reason, both sources related |
| No usable item, an item excluded through a read | `Excluded` (new) | The declared result depends through *read value* on a value inferred in this definition; related: the read value |
| No usable item, own value in the result or key | `Dependency` / `Input`, as today | Today's reasons |
| No item | `Missing`, `Ambiguous` or `Shape`, as today | Today's reasons |

The diagnostic code stays `AssociatedTypeInferenceFailed_Kd`; each failing value reports once at its conformance, as
today. DIAGNOSTICS records the new reason and related location in the implementing unit.

### 4.6 Stage contract

The value stage stays where `PrepareAssociatedInference` runs (Binding.cs:461). The driver visits every hole; then
today's publication of `AssociatedStorage`, `InferenceStorage` and inherited inference runs unchanged. The stage keeps
premise-only judgment (§2); `VerifyConformance`'s collecting guard is deleted, not revived. After the stage every node
is Closed, so later reads use no frame.

### 4.7 Contract for premise supply (PS1's lifecycle gate)

This section is the answer to PS1's blocker, and PS3 implements against it:

1. A supplier request made inside a frame inherits the frame. Each nominal value it normalizes passes
   `ReadAssociatedValue` and gets a final verdict; an Excluded or Failed value stays an unresolved projection and the
   request answers as it would for any unresolved projection (Unknown, never Refuted, §8.3.2 closed world).
2. A Wait abandons the whole evaluation; the request's answer is discarded with it. Request-local storage (premise plan
   §4.4) guarantees nothing of it survives.
3. Outside frames the request reads Closed values only, or Not ready before the stage, whose results callers never
   cache as complete.
4. No supplier result depends on which consumer asked first: within one definition every verdict is stable (§4.3), and
   across definitions a result is computed only in its own frame.

So "contextual evidence exclusion" is the frame's verdict, and the "readiness transition" is replaced by Wait and retry,
which never exposes a value before it is final. No new persistent cache is added.

### 4.8 Caches

| Cache | Treatment |
| --- | --- |
| Definition batch `State`, `Active`, `collectingAssociated` | Deleted |
| `batch.Normalized`, `requirementMatches` | Kept per definition context with read spans; replayed on a hit; not written by an abandoned attempt |
| Completed headers and syntax Types | Unchanged rule: complete only without an unresolved projection, so a frame-dependent result never completes |
| Explicit `State`/`Result` | Cached only for context-free normalizations (§4.2) |
| `pendingAssociatedTypes` | Kept as a structural memo; the per-batch clear goes |
| `inferenceConsumers`, `InvalidateAssociatedEvidence` | Unchanged: they downgrade verification along same-definition and inheritance edges; a value read across definitions needs no invalidation (R2; its projection obligation stays in `projectionUses`) |
| Capability results, conformance verification | Not used during the stage (premise-only), unchanged |

### 4.9 Deletions

`AssociatedInference.State` and `Active`, `collectingAssociated`, the nested `InferAssociatedTypes` recursion and its
sweep loop, the hole branch's batch logic (AT.cs:516-522), the `VerifyConformance` collecting guard
(ContractMatching.cs:157-160), the explicit-candidate reset (AI.cs:156-161) and the per-batch clear of
`pendingAssociatedTypes`. No fallback path remains: the old batch reading is not kept beside the scheduler.

### 4.10 Order independence

The SCCs of a fixed read graph do not depend on the walk's start or order, and the verdict of a read depends only on
the SCC structure (§4.3). The read graph is fixed by the items' reads, which depend on verdicts only through nested
reads (§4.4). V1 must establish that this leaves exactly one consistent graph: a bounded model enumerates small programs
(values per definition, items per value, sibling and nested reads, own reads, conflicts, failures, explicit
specifications) and compares (a) the scheduler under every start order and permutation with (b) a declarative oracle
that enumerates every graph consistent with the rule. Any difference or a second consistent graph stops the work and
returns to the owner with the counterexample. The prepared remedy is a located `Unsupported_Kd` for a nested read on
another definition's value, which the owner would then choose or reject; no code fallback is written in advance.

## 5. Units

Units follow AGENTS (design, reproducer, implementation, focused tests; commit and push per unit). The premise plan's
PS0 evidence is extended, its PS1 bounded evaluator continues, and PS2/PS3 start after V3.

| Unit | Change | Completion condition and checks |
| --- | --- | --- |
| V0 Evidence | Record the Appendix A probes on the implementing head; add the reasoned cases D4 (key read), D5 (dropped pending source), D7 (order after an edit), D11 (chain of 10^4) and D14 (completion under the blackout) as probes; measure token sizes and hotspots of the four touched files; baseline time and allocation of `ScalingWorkloadsPreserveExplicitAndInferredOutcomes` and `AssociatedInferenceMeasurements` | Observations recorded in artifacts; failing desired regressions kept out of the tree; no compiler change |
| V1 Model | `draft/Sketches/G170 Values/` bounded model and oracle of §4.10, in the style of `LoanIdentityByBase.mjs` | Zero differences over every order on the enumerated programs; exactly one consistent graph each; result reported with counts. This, with §4.7, closes PS1's lifecycle gate |
| V2 Restructure | Behavior unchanged: split value reads into `Binding.AssociatedValues.cs`, route every hole read through `ReadAssociatedValue` (still batch semantics); split files due under ARCH limits | Unchanged diagnostic, semantic HIR/MIR and generated-fixture snapshots (VERIFICATION, behavior-preserving commits) |
| V3 Cutover | §4.2-§4.9 in one behavior unit: scheduler, frames, read spans, `Excluded`, explicit transparency, deletions; DECISIONS entry for the representation; DIAGNOSTICS reason | §6 regressions pass and fail before; all existing associated inference, identity and refinement tests; warm allocation tests at 0 B; `AssociatedMetrics` stable across rebinds (its field list updated with the new storage); Unit run, then Session |
| V4 Measurement | Cross-definition workloads (chain, mesh, PA/PB pairs, SCCs) in `tests/Workloads/AssociatedInferenceWorkloads.cs` and the existing benchmark | Time, allocation and retained memory against V0 on equal semantic work; no regression accepted without a recorded reason; Session for Benchmark |

V2 and the premise plan's PS2 touch disjoint files except `ContractMatching.cs`; they may run in separate worktrees.
PS3 then implements §4.7 instead of designing its own lifecycle. T0-T3 keep their gates.

## 6. Tests

New methods in `AssociatedInferenceTest` (owner of these rules), using its helpers; each fails on the baseline:

| Test | Asserts |
| --- | --- |
| `SpecExampleInfersMutualValuesInEveryOrder` | PA/PB in both orders: X=i64, Z=i32, Y=i64, W=i32, both verified |
| `ValuesThroughOtherDefinitionsAreExcludedInEveryOrder` | excl in both orders and tri in all six: the current definition's value fails with `Excluded` naming the read value; the others are i32 |
| `TrueValueCyclesFailInEveryOrder` | cyc in both orders: both values fail, both diagnostics |
| `AlternativeSupportFollowsReads` | alt in both orders with the same outcome (D.X=i32, E.W `Excluded` under g2); explicit `associate Q.W is i32` counterpart valid |
| `KeyReadsAreDependencies` | D4: an implementation parameter reading another definition's value; same outcome in both orders |
| `EverySourceIsResolvedBeforeAValueIsUsed` | D5: a conflict between an independent source and a cross-definition source is reported in both orders |
| `ExplicitSpecificationsReadInTheReadersContext` | g6: a specification naming another definition's inferred value |
| `LongChainsUseNoHostRecursion` | the `external` workload axis at 10^4 definitions binds with the same outcome as at 12 |

Existing tests stay unchanged, including `SameNominalUnitNeverChainsInference`,
`IndependentExternalTypeValuesDoNotDependOnDeclarationOrder`, `NominalMutualReferencesNeedNoConformanceCycle`, the
warm allocation tests and `EditingResultsInvalidatesEvidenceAndRecoversWithoutOldValues`. A `spec-cases.json` row for
§8.5.5's example is added only if VERIFICATION's no-duplicate rule allows it beside the xUnit test.

## 7. Size and performance

Planning estimates, to be replaced by V0/V3 measurements:

| Area | Estimate |
| --- | --- |
| `Binding.AssociatedValues.cs` (new) | About +220 lines: contract, read rule, driver, frame, reach |
| `Binding.AssociatedInference.cs` | About -150 / +40: batch machine and sweep nesting go; read spans and per-identity extraction come |
| `Binding.AssociatedTypes.cs`, `Binding.ContractMatching.cs` | About -15 / +5 |
| Net | About +100 lines |

Scheduling is linear in values plus reads; each SCC closes once; reach unions cost one bit-set word per 64 inferring
definitions. Programs without cross-definition reads do the same work as today. Cross-definition chains replace nested
batches with heap stack entries and at most one retry per edge. Memory per value is three integers and a span; per
evidence one span. Every arena is per pass with reused capacity, so the warm 0 B tests constrain the design and are
not relaxed.

## 8. Owner decisions

| Item | Needed before | Question |
| --- | --- | --- |
| OD1 Representation | V2 | Approve the value graph of §4 as G170's PS1 representation (the owner's choice among representation, whitelist and hold) |
| OD2 Reading of "depends" | V1 | Reads (g2, unique, alt rejected in both orders) rather than usable evidence (alt accepted under one of two fixed points) |
| OD3 SPEC wording | V3 | Explicit instruction to record Appendix B in §8.5.5; until then the implementation follows this plan's reading and STATUS states it |
| OD4 Acceptance | V3 | D5/O3 disposition: the PA/PB family becomes accepted in every order; alt becomes rejected in every order (accepted today when E is first); excl/tri diagnostics change |
| OD5 Process | V3 | This plan had no competing design or independent review (owner instruction). Q5 asks for both before a shared-representation change; the owner either waives them or orders the design review before V3 |

No new diagnostic code, Resource code or closed-vocabulary entry is needed: the walk is bounded by the number of values,
so no work budget exists (§8.5.5 "Acceptance never depends on an inference work budget"). `InferenceProblem` is not a
closed vocabulary.

## 9. Not solved here

| Item | Owner |
| --- | --- |
| G170's abstract premise supply (`T.(Tree).Child.(Tree).Child`) | The premise plan's PS3, now against §4.7 |
| Premises mutated inside requests (`AddContractPremises` from header binding, override premises) | PS3's deletions |
| `projectionUses` duplicates from re-bound pending headers (Contracts.cs:864-869, AT.cs:231) | New open issue; V0 measures |
| Completion evidence lost when a condition needs a nominal conformance or capability under the blackout (AI.cs:321-327) | V0 probes it; a repair belongs to the stage contract, not to this scheduler |
| Transferable (G124) | T0-T3 after PS5 |

## 10. Document maintenance

| Document | Trigger |
| --- | --- |
| DECISIONS | V3 records the value graph, frames, read rule and the `Excluded` problem; OD1-OD5 as answered |
| PLAN, ARCH R4 row | When the owner answers OD1: G170's next action becomes V0-V3 before PS2/PS3 |
| DIAGNOSTICS | V3's reason and related location |
| STATUS | V3: SPEC §8.5.5's example supported; the reading of g2 stated until OD3 |
| SPEC | Only on OD3's explicit instruction |
| INTEGRATED | This intake, in this commit |

## Appendix A. Probe sources

Each probe is a `Demo.kimiproj` application project with this `main.kimi`, checked with `Kimi.exe check`.

```kimi
// PA/PB (SPEC §8.5.5's example); pbpa swaps the two structs.
contract P
    associate X
    associate Z
    func x(self: ref/Self) -> Self.X
    func z(self: ref/Self) -> Self.Z

contract Q
    associate Y
    associate W
    func y(self: ref/Self) -> Self.Y
    func w(self: ref/Self) -> Self.W

struct PA
    Self is P
    public func x(self: ref/Self) -> PB.(Q).Y => 0
    public func z(self: ref/Self) -> i32 => 0

struct PB
    Self is Q
    public func y(self: ref/Self) -> i64 => 0
    public func w(self: ref/Self) -> PA.(P).Z => 0
```

```kimi
// excl: Q has only Y. A.x -> B.(Q).Y, A.z -> i32; B.y -> A.(P).Z. tri adds C: B.y -> C.(Q).Y, C.y -> A.(P).Z.
// cyc: P {X; x()}, Q {Y; y()}; A.x -> B.(Q).Y; B.y -> A.(P).X.
// alt: P {X; a() -> Self.X; b() -> Self.X}, Q {W; w() -> Self.W}
struct D
    Self is P
    public func a(self: ref/Self) -> i32 => 0
    public func b(self: ref/Self) -> E.(Q).W => 0

struct E
    Self is Q
    public func w(self: ref/Self) -> D.(P).X => 0
```

```kimi
// nonrec (G170 without a recursive Contract); nonrec-hint adds
// `hint: ref/T.(Outer).Inner.(CopyHolder).Item` as a second parameter.
contract Holder
    associate Item

contract CopyHolder: Holder
    Self.(Holder).Item is Copy

contract Outer
    associate Inner is Holder

contract CopyOuter: Outer
    Self.(Outer).Inner is CopyHolder

func inspect<T>(value: T.(Outer).Inner.(Holder).Item) -> i32
    T is CopyOuter
    let a = value
    let b = value
    return 0
```

## Appendix B. Proposed §8.5.5 wording (needs OD3)

After "...supplies no evidence." in the paragraph on the current definition:

> A value is determined when each of its evidence items is usable, excluded or unusable. Evidence depends on every
> value read to identify its implementation, normalize its result and prove its source premises, whether or not that
> value's own evidence is usable; a read that needs a value that is not available stops there. A judgment made while
> values are determined uses the frozen premises and concrete identity, not the conformances or capabilities of
> nominal Types. The formation and conformance obligations of a projection in a signature are checked separately and
> never take part in identifying or completing the conformance that declares the signature. A specification that names
> another definition's projection is normalized as its reader's evidence is.
