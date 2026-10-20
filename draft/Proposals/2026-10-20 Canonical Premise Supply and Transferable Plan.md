# Canonical Premise Supply and Transferable Plan

Status: 一部取り込み (open; implementation design, no specification integration).
Owner direction: 2026-10-20, shared premise supply (option A); resolve G170 before connecting Transferable.
Scope: the [G170 PlanB](2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) units C0–C4 replace this
plan's premise-supply design (§§2–4), units PS0–PS5 and their matrix (§7.1); that text remains at `f373cd67`. This plan owns
Transferable: §5, units T0–T3 and §7.2. Section numbers are kept for existing references.
Baselines: investigated at `64649f97`; revised at `338831f7` against the Principles (§9). Line numbers name `338831f7`.
Holds: R4-M83 holds only T1/T2 (2b-4/2b-5), until C4's verified supply exit and T0's gates pass; T0's preparation proceeds now.
R4-M79's BU/R5 pause and the 2b-D gates remain. No SPEC change is proposed.

## 1. Required meaning and evidence

The required chain:

> SPEC §15.2.4 defines Transferable structurally, once per declaration, with abstract Types needing premises → Kimi declares no
> Transferable, and MIR approximates it by a raw/`rc` walk (G124) → Binding decides each verdict from a finite per-declaration
> summary plus canonical premise supply, under the reading definition's premises; a pure TyCtxt view publishes it and MIR
> consumes it → `MirTypes.HoldsRaw`, its `Raw` walk, `MirStatic.RawHolding` and `MirEffect.RawStaticRead` disappear.

| Authority | Obligation |
| --- | --- |
| [SPEC §8.3](../../docs/spec/08-generics-constraints-and-contracts.md#83-constraint-proof) | Four outcomes under Finality; abstract Types prove only from premises; Resource failure on exhausted proof resources. |
| [SPEC §8.7.1](../../docs/spec/08-generics-constraints-and-contracts.md#871-compiler-intrinsic-contracts) | Transferable is a compiler-intrinsic Contract; `Self is Transferable` in a struct or enum declaration is an error, in a Contract a requirement; a user Contract of that name gains nothing. |
| [SPEC §8.11.2](../../docs/spec/08-generics-constraints-and-contracts.md#8112-confined) | Reading or borrowing an immutable static is no environment effect only when its Type is proven Transferable. |
| [SPEC §15.2.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1524-transferable) | Visits OwnedOrigins' components; `rc`, Weak of `rc`, raw pointers, common Function Types and erased views refute; only the Kimi collections and standard iterators exempt their internal raw storage; no Owned proof. |
| [SPEC §15.3.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins) | Computed once per declaration as a least fixed point, never by expanding instantiations, non-regular recursion included. |
| [SPEC §22.1](../../docs/spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) | `Kimi.Transferable` is public, with no parameters, parents or members. |
| [DECISIONS R4-M83](../../docs/dev/DECISIONS.md) | Hold 2b-4/2b-5 until proof-input supply is resolved; never assume existing Facts complete or add local cases. |

Evidence (nothing was executed for this plan):

- **G124** (PLAN): `confined` static reads are judged by `MirTypes.HoldsRaw` and by the old effect checker's narrower
  `ContainsRawPointer`, not by Transferable; its acceptance reproducer is unconfirmed.
- **G170** (PLAN): the canonical Copy proof of a deep projection depends on how premises are spelled. Transferable's abstract and
  associated-projection obligations share that supply cause (R4-M83). PlanB C0 owns its current evidence.
- **Source**: no Transferable producer exists in `src/Kimi` (§5.2). The R4 intake
  `artifacts/handoff/2026-10-20-r4-resume/transferable/dependency-intake.md` inventories the producers and consumers.
- G170 is a missing proof, G124 an unsound classification; neither is closed by this plan alone.

## 2–4. Premise supply (replaced by G170 PlanB)

Transferable consumes C4's contract; T0 checks it against C4's final form:

1. Abstract obligations — `T is Transferable`, and Transferable of associated projections such as `T.(Tree).Child` — are judged
   by `ProveConstraint` (structural subjects by the Transferable judgment), which reads evidence through PlanB's proposition entry
   operation, independent of spelling. PlanB does not name associated-projection Transferable subjects; T0 confirms that C4 covers
   them.
2. A supplied answer is complete, provisional or Resource. A provisional answer (a declaration still pending) is neither cached nor
   published, and T1 judges again after completion. A Resource answer follows the producer failure path, never an ordinary Unknown.
3. No Transferable-only premise table, scan or cache. Capability purpose keys and structural fixed points keep their owner.
4. PlanB owns Resource failures and any diagnostic vocabulary for them. This plan proposes none; its earlier
   `ConstraintProofResourceLimit_Kd` proposal is withdrawn here.

Acceptance changes caused by the supply itself take PlanB's D5/O3 gate before C3.

## 5. Transferable connection after G170

### 5.1 Finite structural summary (T0)

The common supplier answers abstract obligations; it does not give Transferable's finite structural domain. T0 prepares now
(census, oracles, a closed-Type prototype); its supply-coverage check and completion follow C4's verified exit, before 2b-4 resumes.

Candidate: a per-declaration summary over the declaration's formal Type parameters and slots, holding (a) the formals whose
Transferable proof it requires, (b) the abstract obligations it forwards, with their source Contracts, and (c) an exclusion. It is
the least fixed point from the bottom (no requirement, no obligation, no exclusion), with substitution mapping a component's
summary back onto the enclosing formals. T0 defines exact node and edge meaning, substitution composition, finite cardinality and
monotone transfer functions; drawing a finite declaration graph is not enough.

Example (the SPEC expectation, not executed):

```kimi
struct Nest<T>
    let item: T
    let deeper: Array<Nest<(T, T)>>       // Non-regular: each level pairs T.

struct Linked<T>
    let item: T
    let next: Option<rc/Linked<T>>        // rc refutes for every T.
```

- Summary(Nest) = requires {T}. `Array` exempts its own storage and visits `Nest<(T, T)>`, whose requirement on `(T, T)` maps
  back to T. The iteration stabilizes on Nest's own formals, never instantiating `Nest<(T, T)>`, `Nest<((T, T), (T, T))>`, ...
- Summary(Linked) = excluded: `rc` refutes whatever T is.
- `Nest<i32>` is Proven; `Nest<rc/Item>` and `Linked<i32>` are Refuted; `Nest<U>` for an abstract U needs `U is Transferable`
  from C4's supply.

Rules for the design:

- The prototype covers regular and non-regular recursion, changing Type arguments, recursive associated obligations, negative and
  unknown abstract evidence, and the exemptions behind library storage.
- If an obligation cannot be represented finitely, keep the hold and redesign the representation: no depth cap returning Proven or
  Unknown, no silent whitelist, no copy of the capability growth guard (`Binding.Capabilities.cs:228`).
- The summary is finite by construction, so Transferable needs no Resource limit of its own; Resource answers come only from supply.
- The summary reads declared components in terms of the formals (`AdtDef`'s declaration storage, `TyCtxt` interning), never the
  instantiated storage of growing instances, which expands instantiations (§15.3.5) and meets G128's storage bound.

Shared structure. §15.2.4 visits OwnedOrigins' components, and Owned shows the defect a copied guard would bring: the growth
guard returns Unknown at the second growth step, and `IntrinsicCapabilityTest.ExpandingGenericRecursionRemainsUnknownWithoutUnboundedExpansion`
pins Owned of `Chain<i32>` (Field `obj/Chain<(T, T)>`) as Unknown where §15.3.5 requires Proven (PLAN G174). T0 compares:

| Option | Consequence |
| --- | --- |
| (a) A Transferable-only summary | Smaller first step; leaves two component walks, so the second walk needs a removal condition; G174 then needs its own unit. |
| (b) One component walk for OwnedOrigins, shared by Owned and Transferable | Would repair G174 and delete the guard if no other capability, such as Copy, relies on it. A shared-representation change: the full process, its own D5/O3 disposition for Owned's acceptance change, and BU's capability engine, paused under R4-M79. |

### 5.2 Full Type and library contract

T1 adds the declaration and wires it at these sites (Capabilities' leaf tests, `Binding.InheritedConformances.cs:72`,
`Binding.cs:417`, `:513` and `Binding.Access.cs:427` handle any `Intrinsic != None` generically):

| Site | Change |
| --- | --- |
| `Library/Core.kimi:1` | `public contract Transferable`, beside the six other intrinsics (§22.1). |
| `IntrinsicKind` (`KimiLibraryKinds.cs:14`), `KimiDeclarationId` (`KimiDeclaration.cs:7`), `KimiLibraryCatalog.cs:42`, `:77`, `KimiLibrary.cs:162` | Intrinsic identity, catalog entry and accessor. |
| `Binding.Constraints.cs:283` | Dispatch to the Transferable judgment. |
| `Binding.Constraints.cs:477` | A Contract's `Self is Transferable` is a Self-dependent requirement and premise, like Copy and Owned. |
| `Binding.Contracts.cs:452` (`Register`), `Binding.ConditionalConformances.cs:209` | `Self is Transferable` in a struct or enum, conditional or not, is an error (§8.7.1). `Register` closes its family, restructured first with behavior unchanged: None, Copy and Owned register; every other intrinsic fails with its current code (Transferable takes Sealed's `InvalidConstraint`); the `not ObjectPayload` opt-out branch stays. |
| `Binding.ContractMatching.cs:225` | An ancestor conformance to Transferable (`contract C: Transferable`) is decided by the Transferable judgment, as Sealed's is by its capability; never by declaration or member matching. |

A user Contract named Transferable gains no intrinsic behavior; abstract `T is Transferable` uses the ordinary input-constraint
path.

Components follow SPEC, not `HoldsRaw` or Loan holding: Semantics targets, Type arguments (unused and inherited ones too), bases,
Fields, every Case payload, Tuple components, array elements, concrete object payloads, closures and Function Item bound
arguments. A callable signature alone stores nothing. Common Function Types and base or runtime-Contract views refute. Borrow
Origins create no Owned requirement. A reachable `rc`, Weak of `rc` or non-exempt raw pointer refutes, also through `ref` and `arc`.

The raw-pointer exemption attaches to the exact identities of `Array`, `Dictionary`, `Slice`, `UniqSlice` and the standard
iterators and their internal storage, not to a user shape or all descendants: `Array<rc/X>` is still refuted. An empty container,
inactive Case or zero-length array keeps its component obligations. Async execution and workers are outside this plan.

**Default pipeline.** T1 makes `Kimi.Transferable` resolvable, so programs constrained by it become accepted on the default
pipeline (D5). T0's package obtains the O3/OQ7 disposition (a SPEC-conformance fix naming §15.2.4 and §22.1), or an owner-approved
located `Unsupported_Kd` guard until R6-check. The old effect checker's `ContainsRawPointer` (`Binding.EffectBounds.cs:478`,
deleted at R6-check) keeps its behavior; changing it is a separate acceptance decision.

### 5.3 Publication and MIR consumption

Binding decides the verdict under the right declaration and definition premises; a pure TyCtxt view publishes it. An Error or
Resource verdict follows its producer failure path, never becoming an ordinary Unknown static; a provisional verdict at publication
is `InternalInvariant_Kd`. MIR consumes a valid published verdict; it never inspects syntax, repeats proof or repairs a broken
contract.

**Proof anchor (T0).** Fix the anchor and substitution before choosing the publication shape: the static's bound Type, the reading
body's `EffectPremises(definition)` (`Binding.EffectObligations.cs:119`, inherited implementation premises included) and any
selected Semantics case must agree with SPEC. `MirStatic` is body-local while A4 runs per `MirCases` view, so one verdict per
entry suffices only if T0 proves it across views. Compare one generic Type in two definition scopes, inherited contexts and mixed
pair admitted sets. If cases need distinct facts, T0 obtains review and approval for producer-owned per-case publication before
T1/T2; MIR never recovers a missing fact by re-proving it, nor classifies a proven case as Environment to cover a missing per-case
fact.

Current path:

```text
MirBuilder.Places.StaticRoot (:159; also MirBuilder.Opaque.cs:154)
  → MirStatic(Symbol, Type, Mutable, RawHolding)            MirForms.cs:214
  → MirDescriptors: StaticRead | RawStaticRead if RawHolding && !Mutable   :379
  → MirEffects: RawStaticRead folds into Environment                       :337
  → MirDump prints " raw"                                                  :95
```

T2 under T0's approved shape:

- `RawHolding` becomes the published verdict (the existing `ConstraintProof`); the dump prints it.
- An immutable read not Proven is `StaticRead | Environment`. `MirEffect.RawStaticRead` and its fold are deleted; no new effect
  kind. Mutable-static and initializer summary rules stay. Environment's comment ("State outside the program") is widened to
  SPEC's environment authority, including this access to program storage.
- `MirTypes.HoldsRaw` and its private `Raw` walk go after a fresh census (their only reader is `StaticRoot`). Replace their
  approximation tests at the new owner: `MirTypesTest.RawAuthorityWalksEachTypeOnce` (`:102`), the mixed assertion at `:131` and
  `MirValidatorTest` (`:112`). `MirEffectsTest`'s raw-static case (`:33`) stays valid under SPEC and gains the G124 cases. Keep
  `HoldsLoans`, `MirWrites`, the G148/P13 guards and the destruction tests.
- Review the representation with R5 under the shared-file hold protocol (W1 slot S-e, AR-1/AR-2).

**S5** is a separate connection: verify G170's canonical proof and the A1/A4 effects at its implementing head, then record S5's
expected matrix. S5 can widen a weaker callee's summary even when a caller knows Copy, so repairing G170 does not promise that the
frozen helper-chain examples become accepted. G124's closure and F5 Landed still need the other F5 units and 2b-8; this plan does
not absorb static keys, M69, G136 or other effects work.

## 6. Units

T identifiers are local; the A5 mapping is explicit. A unit starts only after its prerequisite design passes independent review
and the applicable owner gates; edits, builds, checks and commits stay serialized per worktree.

| Unit | Change and owning files | Completion condition / checks |
| --- | --- | --- |
| T0 — finite design (2b-D input) | Now: census, oracles and a closed-Type prototype of §5.1, comparing options (a) and (b). After C4's verified exit: prototype with real proof inputs and confirm C4's coverage (§§2–4). Fix the proof anchor and publication shape (§5.3); add the source/shape/census inventory and the default-pipeline disposition (§5.2) to the F5 design package. | Finite-summary argument and adversarial prototype pass; BU/R5 design review, OQ2 approval and applicable vocabulary/O1/O3 approvals. Only then lift the 2b-4/2b-5 hold. |
| T1 — proof (2b-4) | §5.2's declaration and sites, the per-declaration summary and `TyCtxt.Transferable` publication; no MIR consumer. A behavior-preserving split first if a touched file is due. | Public lookup, structural denial of user declarations, abstract/recursive/exemption tests, `KimiLibraryShapeTest`, warm/cold/retained measures and census pass. Record the representation and intrinsic identity in DECISIONS and the verified support in STATUS (LIBRARY already declares it). |
| T2 — MIR (2b-5) | §5.3's verdict, Environment classification, dump; delete `RawStaticRead` and the raw walk. Shared-file/R5 slot review. | MirBuild/Effects/Validator/Types/Dump checks, G124 failing-before regressions, goldens/census and the sel pending check; no Binding re-proof in MIR and no raw approximation reader left. |
| T3 — F5 closure (2b-8) | Combine T2 with 2b-3 and 2b-6 evidence and the existing F5 obligations. | 2b-8's ledger, verified SPEC examples and census pass, then the final F5 review, before F5 Landed or G124's closure. R4/R6 exit obligations remain. |

## 7. Verification gates

### 7.1 Premise-supply matrix

Replaced by PlanB §7.

### 7.2 Transferable matrix

Cover each §15.2.4 exclusion and Kimi exemption with an invalid neighbor:

- `rc`, Weak and raw through `ref` and `arc`; common Function versus a concrete Function Item or closure; base/runtime view versus
  a concrete payload.
- Arbitrary versus proven abstract Types; an ordinary shadowed `Transferable`; user opt-in and opt-out rejection;
  `contract C: Transferable` with an `rc`-holding conformer (rejected) and a transferable one (accepted).
- Collections with transferable and excluded elements; unused and inherited arguments; all Case payloads; empty and zero-length
  components; regular and changing recursive storage (§5.1's examples).
- Malformed source (Error) apart from a missing proof (Unknown).
- Pair Semantics cases, two reading-definition scopes and inherited implementation contexts, before a single per-static verdict is
  treated as sufficient.

Owners: `IntrinsicCapabilityTest`, `TyCtxtTest`, `KimiLibraryShapeTest`, `MirEffectsTest`, `MirBuildTest`, `MirValidatorTest`,
`MirDumpTest`, `MirTypesTest`. Replace the raw approximation assertions rather than keep a second Transferable contract; new
summary tests belong at its producer.

### 7.3 Commands and evidence

Feedback uses an incremental test build and selected methods. Completion uses `scripts/verify.ps1` (`ArchitectureRulesTest` is
added automatically), for example for T1/T2:

```powershell
$transferableClasses = @(
    'IntrinsicCapabilityTest', 'TyCtxtTest', 'KimiLibraryShapeTest', 'MirTypesTest',
    'MirEffectsTest', 'MirBuildTest', 'MirValidatorTest', 'MirDumpTest'
)
./scripts/verify.ps1 -Mode Unit -Class $transferableClasses
```

Add the touched owners from the final census; keep allocation coverage. Every selector must run tests from the newly built
assembly. T2 also runs the affected sel Unit (`-Pipeline mir -Codegen mir`) and records pending-ledger differences under R5-M8
without rewriting R5's ledger. Run each unit's +cen and +led measures, 2b-8's verified examples and explicitly selected milestones;
Session selects no native fixtures or milestones implicitly. Add native checks and O2 measurements only where the diff or the F5
plan requires them; no NativeAOT without explicit instruction. Behavior-preserving commits compare diagnostic and semantic
(HIR/MIR) snapshots and generated fixtures under VERIFICATION. Associate exact inputs with `verify-commit.ps1`, then push without
force. This documentation revision needs only diff/reference review.

## 8. Size, cost, ownership and documents

Planning estimates, not commitments:

| Area | Expected change and cost |
| --- | --- |
| Transferable proof | 2b-4's +80 omits the public intrinsic, §5.2's sites and the finite summary; re-estimate at T0 for option (a) or (b). Library binding gains one declaration per compilation; measure warm and cold Bind. |
| Summary | One summary per declaration, computed once per Binding pass and reused by every instantiation; no per-instance memo. |
| MIR consumption | Net reduction: `RawStaticRead`, `HoldsRaw` and the `Raw` walk go; the `MirStatic` field changes meaning, not size. |

Measure cold/warm time, allocations and retained state by declaration width, recursion depth and instantiation count; compare
equal sources and configurations in matched runs; keep failed runs. Before editing, T1 measures tokens and hotspots of the files
it touches and splits due files first (ARCH limits); no exceptions are added here.

R4 coordinates the A1/A4 and F5 consumers. BU or a specifically assigned implementer owns the Binding producer; R5 reviews T2 and
BU the producer contracts. Implementation prompts carry this design, the unit boundaries, deletions, oracles and checks; a design
mismatch stops implementation for a planning decision.

| Document | Update trigger |
| --- | --- |
| DECISIONS | First implementation of the summary, the intrinsic identity, the publication shape and the default-pipeline disposition. |
| COMPILER_ARCHITECTURE / CODEMAP | Actual owner and stage changes. |
| PLAN / STATUS | Actual next action, blocker or verified boundary; G124 and G174 stay open until their evidence passes; STATUS's Async row records Transferable's verified support in T1. |
| LIBRARY | Already declares `contract Transferable`; changes only if T1 changes a public guarantee. |
| SPEC / IMPL | No change; a discovered gap is reported separately. |

SETTLED constraints stay: no added spelling for constraint identity, no hidden use conditions from generic instantiation, no change
to defaults' interpretation or premise context.

## 9. Review record

Independent design D (selected) and planning design F were written by different agents; two independent reviewers passed the
revised document on 2026-10-20 after four blocking clarifications (capability purpose keys, concrete recursive Contracts, per-case
static verdicts, indirect readers) and the default-acceptance gate were applied. PlanB later replaced the supply design. The
original record is at `f373cd67`.

### Revision (2026-10-20)

The owner asked for the plan to be scrutinized against the Principles. Claims were checked at `338831f7`:

| Finding | Principle / rule | Correction |
| --- | --- | --- |
| The header deferred to PlanB, but §§2–4, §6, §7.1, §7.3 and §8 still prescribed the replaced supply design (`Binding.Premises.cs`, PS units, "after PS5"). | One Concept, One Canonical Form | Supply text removed (kept at `f373cd67`); §§2–4 state the contract Transferable consumes from C4; PS references become C4. |
| A Resource diagnostic name was proposed beside PlanB's own Resource contract. | One owner per decision | Withdrawn; PlanB owns it. A finite summary needs no Transferable limit. |
| T1 listed only Core, `IntrinsicKind` and the catalog; the Contract-requirement path, `ConditionalConformances` and `ContractMatching` also decide intrinsic conformances, and `Register` falls through silently for unlisted intrinsics. | Local Reasoning (census); closed families | §5.2's site table, with `Register`'s closed rule. |
| T1's public declaration changes default-pipeline acceptance without a D5/O3 disposition (OQ7 names 2b-5, not 2b-4). | Explicit Semantics; ARCH D5 | §5.2 and T0 require it. |
| §5.3 named no location or owner for `ContainsRawPointer`: Binding's old effect checker, deleted at R6-check. | Consistent | Stated. |
| §5.3 missed `StaticRoot`'s Opaque caller and the raw tests; §8 called the `Raw` walk unused. | Verifiable Changes | Path, tests and §8 corrected. |
| Owned's growth guard pins a non-SPEC Unknown in a test; the plan offered no shared-structure option. | One Concept; AGENTS (two options) | PLAN G174; T0 compares a Transferable-only summary with one shared component summary. |
| §5.1 had no concrete example of the finite summary. | Explicit Semantics | `Nest`/`Linked` example; one summary per declaration per Binding pass. |

No compiler, build, test or model ran. The hold, T0–T3's order and the owner gates are unchanged.

Independent reviewer `review_transferable_revision` (author of neither) checked every anchor, the example and the D5/O3 claim;
round 1's two blocking findings (the Contract-requirement path, where `contract C: Transferable` could pass by member matching; the
§§2–4 interface, which merged PlanB's evidence supply with judgment) were rewritten as above, and round 2 accepted with wording
points (`Register`'s current codes, a balanced options table, G174's owner), applied.
