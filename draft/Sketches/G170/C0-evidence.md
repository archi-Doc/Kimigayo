# G170 C0: exact-head evidence

Unit C0 of [PlanB](../../Proposals/2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) (BU planning
session, 2026-10-21). Evidence only: no production, test-project, SPEC, DECISIONS or PLAN change. Data in [c0/](c0/).

## 0. Head and configuration

- Head `8b2350aaa75ef14c0eb81f25fdfc94d68c6562da` (origin/dev). `src/` and `tests/` equal R4's fact-check head `4b36db41`
  (`git diff --stat 4b36db41 8b2350aa -- src tests` is empty); every result below was re-run at `8b2350aa`.
- Release build of `tests/xUnitTest` and `src/Benchmark` in worktree `bin/worktrees/bu-g170-c0`; SDK 11.0.100-rc.2, .NET 10.0.13,
  Windows 11 x64 (10.0.26300). Other sessions built and verified in parallel (timings below are noisy).
- Probe: a temporary test-assembly method, [c0/G170C0ProbeTest.cs](c0/G170C0ProbeTest.cs), output
  [c0/probe-output.txt](c0/probe-output.txt). Per program two fresh compilations with `Compilation.Pipeline` = `Legacy` (the
  default pipeline: old Analysis decides) and `Mir` (MIR built and analyzed beside it; A1/A4 problems read from
  `BuildMir().Problems`, source module only); `WindowsProfile.Target`, `OutputKind.Application`. No MIR difference ledger run.
- Queries: **lexical** `Binding.ProveCopy(type, function)`; **effect** `TyCtxt.Copy(definition, type)`, i.e. `ProveCopy` under
  `EffectPremises(definition)` (`TyCtxt.Effects.cs:59`, `Binding.EffectObligations.cs:119`), also printed four-valued.
  **Generated**: a projection interned by `TyCtxt.Intern` from the spelled depth-1 projection's symbol and Contract component,
  never spelled in source; `GENERATED-EQUALS-SPELLED True` and `(== spelled value)` show it is the spelled Type itself.
- R4's probes (CLI `Kimi.dll check` and a mirprobe at `4b36db41`), cited and agreeing with everything below:
  `%TEMP%\claude\C--Users-bwff1-repos-archi-Doc-Kimigayo\19481cde-decb-4895-b5de-273035a1c856\scratchpad\eval-1020\probes\`
  (`g170-*`, `nonrec*`, `s5-*`, `papb*`, `pbpa`, `alt-*`, `excl-*`, `cyc-*`, `tri-*`, `*-mirprobe.txt`, `g170-provecopy.txt`).

## 1. G170 reconstructed

Declarations as PlanB §1 (`contract Tree / associate Child is Tree`; `contract CopyTree: Tree, Copy / Self.(Tree).Child is CopyTree`);
`func inspect<T>(<parameters>) / T is CopyTree / let pair = (value, value)`. `value` is `T` followed by `.(Tree).Child` d times; the
stronger spelling is an unused `hint: ref/T.(Tree).Child` followed by `.(CopyTree).Child` d-1 times. Binding: complete, 0 issues in
every G170 program below. Result columns: value's lexical/effect proof; default pipeline (old Analysis) records; MIR (A1) problems.

| Program | value lexical/effect | Generated (lexical/effect) | Default pipeline | MIR |
| --- | --- | --- | --- | --- |
| d1, absent | Proven/Proven | d2, d8 Unknown/Unknown | 0 | 0 |
| d2, absent (PlanB's Type) | Unknown/Unknown | d3, d8 Unknown | TransferRequired 9:17, 9:24; MovedPlace 9:24 | MovedPlace 9:16 |
| d4, absent | Unknown/Unknown | d5, d8 Unknown | same three | MovedPlace |
| d8, absent | Unknown/Unknown | d9 Unknown | same three | MovedPlace |
| d2/d4/d8, stronger before | Proven/Proven | d+1 Unknown | 0 | 0 |
| d2/d4/d8, stronger after | Proven/Proven | d+1 Unknown | 0 | 0 |
| d2, hint `ref/T.(CopyTree).Child.(CopyTree).Child` after | Proven/Proven | - | 0 | 0 |
| d2, hint `ref/T.(CopyTree).Child.(Tree).Child` after | Unknown/Unknown | - | same three | MovedPlace |
| d4, hint covering depth 2 only | Unknown/Unknown | - | same three | MovedPlace |
| d2, value spelled `T.Child.Child` | Unknown/Unknown | - | same three | MovedPlace |
| d2, value spelled `T.(CopyTree).Child.(CopyTree).Child` | Proven/Proven | - | 0 | 0 |
| d2, the hint in another function `other<T>` | Unknown/Unknown | - | same three | MovedPlace |
| d2, body `()` (PlanB §1's form) | Unknown/Unknown | - | 0 | 0 |
| d2, body call `node.child()` (Tree's) on `node: ref/T.(Tree).Child`, before or after the use | Unknown/Unknown | - | same three | MovedPlace |
| d2, body call `node.touch()` declared in CopyTree, before or after the use | Proven/Proven | - | 0 | 0 |
| d2 absent / stronger before, queried generated-first and in reverse | unchanged | unchanged | - | - |
| Non-recursive: `T.(Outer).Inner.(Holder).Item` under `T is CopyOuter` (R4's `nonrec`), absent | Unknown/Unknown | - | TransferRequired 15:13, 16:13; MovedPlace 16:13 | MovedPlace 16:13 |
| Non-recursive, `hint: ref/T.(Outer).Inner.(CopyHolder).Item` before or after | Proven/Proven | - | 0 | 0 |
| S5Probe `two` (Holder.put destroys T.Child.Child) | put's scope: T, T.Child Proven/Proven; T.Child.Child, T.Child.Child.Child Unknown (generated) | | IncompatibleContractImplementation 23:5 | IncompatibleContractImplementation 23:5 |
| S5Probe `one` (destroys T.Child) | same | | IncompatibleContractImplementation 23:5 | 0 |
| S5Probe `twice` | Unknown/Unknown | - | as d2 | MovedPlace 8:16 |

Effect results equal lexical results in every row (four-valued, the value Types are Unknown, never Refuted). Observations:

1. **Spelling, not Type or scope, decides.** The same Type in the same scope is Unknown when absent and Proven when an unused
   parameter spells CopyTree at every receiver below `T`, before or after `value`, at depths 2, 4 and 8. A hint must cover every
   level: CopyTree on `T` only, or up to depth 2 for a depth-4 value, leaves it Unknown; a hint in another function does nothing.
2. **Generated queries lag the deepest written stronger spelling by one level**: with hints to depth d, the generated d+1 is Unknown.
   A4's generated query (S5 `two`, Holder.put) is Unknown for exactly the Type no CopyTree-qualified spelling reaches.
3. **Query order**: binding order (hint before/after, call before/after the use) and query order (generated first, reverse) never
   change an answer. A body statement *after* a use still decides that use, because premises are scope-wide and added while
   binding, before analysis.
4. **Pipelines**: G170 is not MIR-only. The default pipeline rejects every Unknown row as well (TransferRequired/MovedPlace), and both
   S5 helper chains (IncompatibleContractImplementation [Ownership.EffectBound]); MIR A4 passes `one`. The old path's rejection of
   `one` is not explained by the supply (T.Child is Proven in put's scope) and was not investigated.

## 2. The source explanation is confirmed by experiment

Premise dumps of the queried scope (`D` DirectFacts; `d[S]` a DerivedFacts row with source Contract S; probe output `FACTS`):

| Program | Facts of `inspect`'s (or Holder's) scope |
| --- | --- |
| d1 | D `T is CopyTree`; d[CopyTree] `T.Child is CopyTree`, `T is Tree`, `T is Copy`; d[CopyTree,Tree] `T.Child is Tree` |
| d2 absent; CopyTree on T only | the above + d[Tree] `T.Child.Child is Tree` |
| d2 stronger before | + d[CopyTree] `T.Child.Child is CopyTree`, `T.Child is Copy`; d[CopyTree,Tree] `T.Child.Child is Tree` |
| d2 `node.touch()` (CopyTree member) | + d[CopyTree] `T.Child.Child is CopyTree`, `T.Child is Copy`; d[Tree,CopyTree] `T.Child.Child is Tree` |
| non-recursive absent | D `T is CopyOuter`; d[CopyOuter] `T.Inner is CopyHolder`, `T is Outer`; d[CopyOuter,Outer] `T.Inner is Holder` |
| S5 Holder | D `T is CopyTree`; d[CopyTree] `T.Child is CopyTree`, `T is Tree`, `T.Child is Tree`, `T is Copy` |

- **One-snapshot expansion**: `ExpandContractPremises` (`Binding.Contracts.cs:720-738`, from `BindConstraints` `Binding.Constraints.cs:370`)
  expands the snapshot `{T is CopyTree}` (`ExpandScopeContractPremises` copies Facts once, `Binding.Contracts.cs:747-764`); the derived
  `T.Child is CopyTree` is never expanded, so `T.Child.Child is CopyTree` (and `T.Inner.Item is Copy`) is absent.
- **Written-qualifier-triggered expansion**: binding `T.(Tree).Child.(Tree).Child` adds Tree's clauses for `T.Child`
  (d[Tree] `T.Child.Child is Tree`) through `BindAssociatedProjection` (`Binding.AssociatedTypes.cs:230-237`; evidence = qualifier
  ?? reference ?? owner, so the unqualified spelling also picks Tree, the declaring Contract). A CopyTree qualifier on the `T.Child`
  receiver adds CopyTree's clauses; one on `T` adds nothing new.
- **`RequirementMember` is the same trigger** (`Binding.ContractCalls.cs:148-157`): a body call of a CopyTree requirement on a
  `T.Child` receiver supplies `T.Child.Child is CopyTree`; a call of Tree's `child()` expands only Tree. Body statements use the
  function's scope (no separate statement scope appeared).
- A derived row records the expanded Contract only, not its antecedent fact (`T.Child is Tree` has two rows, CopyTree and Tree).

## 3. Supply and read census (Binding, `8b2350aa`; [c0/census-raw.tsv](c0/census-raw.tsv) lists every hit with its method)

Paths are under `src/Kimi/Compiler/Binding/`. Writers:

| Writer | Site | Role |
| --- | --- | --- |
| `AddConstraintFact` | `Binding.Constraints.cs:584-617` | Sole insertion: DirectFacts or a (fact, source) DerivedFacts row, Facts, both presence flags, Invalid on Error, And split |
| `BindConstraint` | `Binding.Constraints.cs:491-499` | Direct inputs |
| `AddContractPremises` (+ `AddArithmeticPremises`, `Binding.ArithmeticContracts.cs:63-75`) | `Binding.Contracts.cs:772-793` | Own/inherited clauses and ancestors; 4 callers: `Contracts.cs:733` (eager Contract-scope Self), `:762` (snapshot), `AssociatedTypes.cs:236`, `ContractCalls.cs:155` (syntax triggers) |
| `ExpandScopeContractPremises` | `Binding.Contracts.cs:740-770` | One snapshot; 6 callers: `Contracts.cs:736` (`ExpandContractPremises`), `ConditionalConformances.cs:256` (`BindConditionalDeclarations`), `ContractMatching.cs:545` (`CompatibleRequirement`), `InheritedConformances.cs:289` (`AddInheritedPremises`), `VirtualOverrides.cs:101` (`PrepareVirtualHeaderPremises`), `:406` (`PrepareVirtualOverridePremises`) |
| Scope composers | `ConditionalConformances.cs:122-133` (`VerifyConformanceAgreement`), `ContractAgreement.cs:32-38` (`ContractOverlapScope`), `:69-85` (`AddOverlapFacts`), `ContractMatching.cs:534-545`, `InheritedConformances.cs:280-289`, `VirtualOverrides.cs:97`, `:399` (shares the body scope's environment by reference), `:411-434` (`InheritVirtualConstraints`) | Copy DirectFacts and DerivedFacts rows, or add substituted clauses, into composed scopes |
| Invalid writers | `Constraints.cs:396`, `:407`, `:610`; `ConditionalConformances.cs:267`; `ConditionalMembers.cs:74`; `ConstraintValidation.cs:404` | Environment invalidity |
| Reset | `BindingModel.cs:641` | Per pass |

Counts: 17 `AddConstraintFact` call sites in 10 methods (8 files); 4 `AddContractPremises` callers; 6 snapshot callers.

Readers by PlanB §2's meanings (scan line in parentheses):

| Meaning | Readers |
| --- | --- |
| Proof and capabilities | `AvailableConstraintFact` `Constraints.cs:196-218` (Facts/DirectFacts membership, DerivedFacts `:208`, `AvailableContractPremise`; 22 calls in 16 files); `ProveConstraint` `:619-681` (Invalid; presence gate `:640`; `:642`); `ValidateConstraintEnvironments` `:384-418` (`:401`); `ProveConformance` `Contracts.cs:664-692` (`:681`); `SymbolicCapability` `Capabilities.cs:522-560` (`:536`); `AdmittedSemantics` `ConstraintValidation.cs:49-66` (`:59`); `VerifyConformance` `ContractMatching.cs:167` (Invalid) |
| Candidate discovery | `FindAssociated` `AssociatedTypes.cs:107-121` (`:114`); `RequirementMember` `ContractCalls.cs:107-120` (`:114`); `HasContractFact` `Indexers.cs:200-216` (`:209`); `SelectValueCall` `CallableSelection.cs:43-72` (`:49` count, `:63`); `TryCallable` `ValueCalls.cs:289-305` (`:296`); `CallableBoundSignatures` `Retention.cs:35-50` (`:45`) |
| Identity and formation | `HasParameterIdentity` `TypeIdentity.cs:12-20`; `SubstituteIdentityPremises` `:31`; `IdentityPremise` `:100-152` (`:122`, `:149`); `ContractTypeCore` `AssociatedTypes.cs:679-690` (`:684`); `NormalizeProofConstraint`/`NormalizeProofType` `Constraints.cs:685`, `:714`; `HasValueRole` `ConstraintValidation.cs:665-680` (`:673`); `ValidateContractDeclarations` `Contracts.cs:884-895` (`:887`); Structs' shortcut `Structs.cs:329` |
| Arithmetic and effects | `ArithmeticNumericDomain` `ArithmeticOperators.cs:39-50` (`:44`); `SelectArithmetic` `:200-210` (`:205`); `ValidateArithmeticDirections` `ArithmeticDiagnostics.cs:51-75` (`:51`, `:68`); `AvailableEffectBounds` `EffectDeclarations.cs:210-225` (`:217`); `AvailableCallableEffects` `CallableEffects.cs:31-45` (clauses x `AvailableConstraintFact`); `EffectPremises` `EffectObligations.cs:119-123`, `TyCtxt.Effects.cs:59` |
| Scope composition | `DisjointConformanceFacts` `ConditionalConformances.cs:16-35` (`:28` x `:30`); `VerifyConformanceAgreement` `:122-133` (`:125`, `:130`); `ResolveConformance` `:277`, `ProveConformanceConditions` `:437`, `ProveMemberConditions` `ConditionalMembers.cs:37` (Invalid); `RefutedOverlapInputs` `ContractAgreement.cs:46-51` (`:51`); `AddOverlapFacts` `:69-85` (`:77`, `:82`); `ValidateAssociatedOverlapAgreement` `:92-100` (`:98`); `PrepareVirtualHeaderPremises` `VirtualOverrides.cs:116-121` (`:121`); `InheritVirtualConstraints` `:411-416` (`:416`) |
| Source validity and inference | `ValidateConstraintUses` `ConstraintValidation.cs:404`; `ValidateConstraintEnvironments` (above). AssociatedInference reads no supply directly: it reads through `ContractType` normalization (its `Normalized` cache) and conformance-path scopes (`witnessScopes`) |

Totals: 32 scan loops (24 over Facts, 5 over DirectFacts, 3 over DerivedFacts) in 27 methods of 17 files; 5 further Count/CopyTo
reads. Presence flags: `ConstraintEnvironment.HasAssociatedProjection` (written `Constraints.cs:606`, read `:640`) and
`HasParameterIdentity` (written `:607`; read through `TypeIdentity.cs:12-20` at `Constraints.cs:623`, `:685`, `:714`,
`TypeIdentity.cs:31`, `:100`, `:117`, `:144`, `Capabilities.cs:522`, `Structs.cs:329`); `Invalid` at every scope walk.

Caches and guards that hold supply-dependent state: `capabilityNodes`/`Edges`/`Queue`/`TypeDepths` (`Capabilities.cs:9-12`; cleared by
`ClearCapabilityResults` `:195`, called at `:192`, `ConstraintValidation.cs:213`, `:219`, `Binding.cs:530`, `:544`; results then persist
into later queries such as analysis); AssociatedInference `Normalized` (`AssociatedInference.cs:722`, used `AssociatedTypes.cs:612-618`);
**TyCtxt `retentions` keyed by scope** (`TyCtxt.Retention.cs:72`, filled from `CallableBoundSignatures`, cleared by `ResetRetentions`
`:128`); composed scopes `conformanceOverlapScopes` (`ConditionalConformances.cs:10`), `contractOverlapScopes` (`ContractAgreement.cs:9`),
`witnessScopes` (`ContractMatching.cs:9`); guards `contractPremiseQueries` (`Constraints.cs:10`, `:175-189`), `normalizingAssociated`
(`AssociatedTypes.cs:10`), `conformanceQueries` (`ConditionalConformances.cs:9`); scratch `contractFactScratch` (`Contracts.cs:748-755`).

## 4. Inference outcomes (Binding only; probe `INFER` lines)

| Program | At `8b2350aa` | SPEC 8.5.5 |
| --- | --- | --- |
| PA/PB, PA first, all 4 requirement orders | PA IncompatibleContractImplementation (`Self is P`) + 2 InvalidConstraint; PB AssociatedTypeInferenceFailed (`Self is Q`) + 2 InvalidConstraint; neither verified | PA.X i64, PA.Z i32, PB.Y i64, PB.W i32 |
| PA/PB, PB first, all 4 | Mirror: PA AssociatedTypeInferenceFailed, PB Incompatible | same |
| alt, D first, both requirement orders | D Incompatible + 2 InvalidConstraint + TypeMismatch; E AssociatedTypeInferenceFailed + 2 InvalidConstraint | D.X i32 (`b`'s E.W excluded for D), E.W i32 |
| alt, E first, both | Accepted: D.X i32, E.W i32 | same |
| excl (A.Item -> B.Value -> A.Other), A first | A AssociatedTypeInferenceFailed + TypeMismatch; B AssociatedTypeInferenceFailed; each + 2 InvalidConstraint | A.X excluded; B.Y i32 by this reading (not asserted) |
| excl, B first | B Incompatible; A AssociatedTypeInferenceFailed; each + 2 InvalidConstraint | same |
| cyc (A.X = B.Y, B.Y = A.X), both orders | Both AssociatedTypeInferenceFailed (mirror) | Error (`CapabilitiesCyclesAndDuplicateDeclarationsRemainErrors`) |
| tri (A.X <- B.Y <- C.Y <- A.Z), 6 orders | 3 per-definition outcomes, decided by the first declared definition; none verified | A.X excluded; B.Y, C.Y i32 by this reading |

Requirement order never changes an outcome; declaration order does (G172). No inference outcome contradicting SPEC was found outside
these G172 programs. Outcomes the frozen view must keep (all pass at the head): `AssociatedInferenceTest` (58 rows) including
`SameNominalUnitNeverChainsInference` (same-definition chain, explicit counterpart), `IndependentExternalTypeValuesDoNotDependOnDeclarationOrder`
(completed external value, both orders), `ExcludedEvidenceCannotCertifyAConformance`, `OtherRequirementsCanMatchAfterCompletionWithoutSupplyingEvidence`,
`ConditionalChildEvidenceNeedsItsPremisesAtTheParent`, `CapabilitiesCyclesAndDuplicateDeclarationsRemainErrors`,
`EditingResultsInvalidatesEvidenceAndRecoversWithoutOldValues`, `ScalingWorkloadsPreserveExplicitAndInferredOutcomes`; and
`AssociatedInferenceBoundaryTest` (21 rows) including `MergedFragmentsFreezeAllConformancesTogether`, `DistinctBoundContractsDoNotShareTheirResults`,
`LateEffectFailureInvalidatesSharedAndExternalEvidence`, `RemovingExplicitEvidenceCannotReuseTheOldFrozenBasis`.

## 5. Sizes, hotspots and baselines

A1 non-trivia C# tokens (the R4 unit-report count, Roslyn from the test bin; [c0/sizes.ps1](c0/sizes.ps1), full table
[c0/sizes.md](c0/sizes.md)); change counts over the last 30 commits touching the 28 measured files (`111e04fb..ff4ed4ef`, 2026-10-18..19).

| File | Tokens | Lines | Of the 30 | File | Tokens | Lines | Of the 30 |
| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: |
| Binding.Contracts.cs | **6,516** | 987 | 0 | Binding.ContractMatching.cs | 4,921 | 727 | 0 |
| Binding.Constraints.cs | **5,324** | 783 | 3 | Binding.EffectDeclarations.cs | 4,884 | 815 | 0 |
| Binding.AssociatedTypes.cs | **5,047** | 784 | 0 | Binding.AssociatedInference.cs | 4,542 | 732 | 0 |
| Binding.ConstraintValidation.cs | 4,159 | 691 | 0 | Binding.Capabilities.cs | 4,001 | 641 | 1 |
| Binding.VirtualOverrides.cs | 3,544 | 559 | 1 | Binding.ValueCalls.cs | 3,543 | 564 | 1 |
| Binding.ConditionalConformances.cs | 3,009 | 473 | 1 | Binding.Structs.cs | 2,993 | 435 | 8 |
| Binding.ArithmeticOperators.cs | 2,161 | 285 | 3 | Binding.CallableSelection.cs | 1,856 | 261 | 3 |
| Binding.ContractCalls.cs | 1,929 | 336 | 1 | Binding.TypeIdentity.cs | 841 | 176 | 1 |

The three bold files already exceed the 5,000-token file limit (COMPILER_ARCHITECTURE "Structure limits"): they may not grow, so C2
splits them before any growth. Hotspots: `Binding.Structs.cs` (8 of 30), `Binding.Constraints.cs`, `ArithmeticOperators.cs`,
`CallableSelection.cs` (3 each). The 28 files total 76,590 tokens.

Baselines at the head (no failure): the §7 classes `AssociatedRefinementCapabilityBindingTest`, `AssociatedSubjectConstraintBindingTest`,
`AssociatedContractParameterTest`, `AssociatedIdentityTest`, `ConstraintBindingTest`, `NormalizedConstraintProofBindingTest`,
`TypeIdentityTest`, `ClosedConformancePrerequisiteBindingTest`, `ProvisionalContractPremiseBindingTest`, `IntrinsicCapabilityTest`,
`AssociatedInferenceTest`, `AssociatedInferenceBoundaryTest`, `ClosedConstraintLifecycleBindingTest`, `LateConstraintEnvironmentBindingTest`,
`ConstraintProjectionCertificateBindingTest`, `UnresolvedCapabilityProofBindingTest`, `MirInitTest`, `MirEffectsTest`,
`DeepRefinementPremiseBindingTest`: Functional 584 passed; Allocation 28 passed, among them `WarmAssociatedRefinementProofsAllocateNothing`,
`WarmInferenceAndRetainedWitnessesAllocateNothing`, `RejectedWarmInferenceReusesStorage`, `WarmDeepRefinementBindingAllocatesNothing`,
`WarmDeepConformanceVerificationAllocatesNothing`, `WarmNormalizedProofsAndCallsAllocateNothing`, `WarmCapabilityBindingReusesWorkStorage`
(2), `WarmProvenanceRevalidationAllocatesNothing`, `WarmPendingCapabilityQueriesAllocateNothing`, `IdentityClosureReusesItsScratchStorage`.
`src/Benchmark --associated-inference` ([c0/associated-inference.json](c0/associated-inference.json), compiler module
`8cd2344a-fd20-4d2c-8102-255a816ba79a`): 48 inputs, all binds succeeded, 240 of 240 warm samples allocated 0 bytes; medians 10.9-24.0 ms
(inferred) and 11.4-27.9 ms (explicit). These timings are noisy (parallel sessions) and are not compared with the retained 2026-10-08
run (6.77-8.20 ms). No premise-specific benchmark exists.

## 6. Desired regressions (fail at the head; kept as data)

Written in their owners, run at `8b2350aa`, then removed from the test project: [c0/desired-regressions.patch](c0/desired-regressions.patch),
the members as text in [c0/AssociatedRefinementCapabilityBindingTest.desired.cs](c0/AssociatedRefinementCapabilityBindingTest.desired.cs)
and [c0/AssociatedInferenceTest.desired.cs](c0/AssociatedInferenceTest.desired.cs), results in
[c0/desired-regressions-at-head.txt](c0/desired-regressions-at-head.txt) (32 rows, 18 fail).

| Method (owner) | Rows | Fail at head |
| --- | --- | --- |
| `RecursiveRefinementPremisesProveDeepProjectionsWhateverTheSpelling(depth, hint)` (AssociatedRefinementCapabilityBindingTest): depth 1/2/4/8 x absent/before/after, lexical Proven and `TyCtxt.Copy` | 12 | 3: depth 2, 4, 8 absent ("Expected: Proven, Actual: Unknown") |
| `RecursiveRefinementPremisesProveGeneratedProjections(depth)`: interned T.Child^depth | 4 | 3: depth 2, 4, 8 |
| `NestedRefinementPremisesProveTheProjectionWhateverTheSpelling(hint)`: non-recursive variant | 3 | 1: absent |
| `IndependentValuesOfAnInProgressDefinitionSupplyEvidence` (AssociatedInferenceTest, G172): PA/PB, 2 declaration x 4 requirement orders | 8 | 8 (Bind incomplete; issues as §4) |
| `EvidenceOnTheDefinitionsOwnValueIsExcludedWhileIndependentEvidenceCompletes` (G172): alt, 2 x 2 | 4 | 2: D first |
| `EvidenceThroughAnotherDefinitionOnTheDefinitionsOwnValueIsExcluded` (G172): A.X fails in both orders, same outcome in both | 1 | 1: the outcomes differ by order |

## 7. Departures from PlanB/PLAN and what remains unverified

- PLAN G170's row (dev) says Binding proves the Type and suspects "the Type form MIR passes". At the head Binding reports 0 issues
  but proves nothing (lexical and effect Unknown), the generated Type is the spelled one, and the default pipeline rejects the same
  programs. The cause is the supply, as PlanB §1-2 states; the PLAN row is stale (C0 makes no PLAN edit).
- PlanB §2's `RequirementMember` trigger holds only for members declared in the stronger Contract (here CopyTree).
- Not named in PlanB §2/§4.4: TyCtxt `retentions` keyed by scope, AssociatedInference's `Normalized` cache keyed by scope, and composed
  scopes holding copied DerivedFacts; C2's census should list them with the capability results.
- No other contradiction found: six snapshot callers, the eager Contract-scope clauses, the source-Contract-only derived rows and the
  G172 orders (§1, §7 of PlanB) match.
- Unverified: no MIR difference ledger run; spelling dependence of consumers other than Copy (Owned, Sealed, lookup, arithmetic,
  identity, effects) was not probed, its census is by reading; the old path's rejection of S5 `one`; B's expected outcome in `excl`
  and `tri` (SPEC reading only); timings (noisy); indirect readers through `ProveConstraint` and helpers are counted by family, not one
  by one.
