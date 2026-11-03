# X-2: Unsupported_Kd census at R4's exit (PD28, OQ9; DEC O4)

Base: `origin/dev` 5b1777f34 (2026-11-03), read-only, `git grep`/`git show`. Paths below are `src/Kimi/Compiler/Mir/` unless stated.
"Pinned" = a test asserting the record (or its absence where unreachable); "none found" = no test located by grep of
`tests/xUnitTest/Tests/Mir*`, `Origin*`, `Closure*` (not proof of absence). Owner: who holds the file/fix (R4 `Mir/Analysis/**`,
R5 W1 for `Mir/Build/**` (released), BU Binding). Copies: `x2work/` (PLAN, STATUS, plans, DECISIONS at the base).

Recorders (one each): builder `Build/MirBuilder.Opaque.cs:29-37` -> `MirProgram.cs:79` (default code); A4 obligation
`Analysis/MirEffects.Solve.cs:110-114`; A5 Loan stratum `Analysis/MirBorrows.Reports.cs:99-111` (`Limit`); A5 Origin stratum
`Analysis/MirOrigins.Chains.cs:571-575`. A1 (MirInit), A2 (MirDrops), A3 (MirLints), A6 (MirReport), MirValidator: **no
Unsupported_Kd producer** (A1/A2/A5 arenas give OwnershipStorageLimit_Kd; the validator gives findings/InternalInvariant_Kd).
The ledger produces none; its MIR-side Unsupported rows: `b-comparison-limit`, `b-identity-places` (e), `a5-p13`, `a5-pq10`,
`a5-row9`, `a5-row9-incomplete` (b).

## 1. Producers

### B. MirBuilder (16 record sites; the derived Opaques `Places.cs:149`, `Transfers.cs:64`, `Values.cs:360` and `Values.cs:194`'s
`Acquire == Unset` branch record nothing and are not counted)

| # | Site | Form (SPEC) | Owner | Removal stage | Pinned |
| --- | --- | --- | --- | --- | --- |
| B1 | `Build/MirBuilder.Calls.Comparisons.cs:17-19` | tuple operator whose Self has no comparison (TyCtxt.Comparison Limit; 13.4, 23.1.1) | R5/BU (G128 cause) | R7 (R5-M152 OI-2, PD30 (e); ledger `b-comparison-limit`) | MirBuildTest.Calls.ATupleOperatorWithoutItsComparisonIsUnsupported; MirBuildTest.Values.UnsupportedFormsAreOpaqueAtOneRecord |
| B2 | `Build/MirBuilder.Calls.cs:51-54` | call plan whose operations do not cover the parameters (7.2, 10) | R5 W1 | none named | none found |
| B3 | `Build/MirBuilder.Defaults.cs:110-123` | omitted default its call's substitution does not instantiate (storage Limit; 7.2.3, 23.1.1) | Binding (G128) | R7 (G128: substitution without storage preparation) | MirBuildTest.Defaults.cs:179-203 rows `lim`, `lim64`, `lnull`, library `take` |
| B4 | `Build/MirBuilder.Operators.cs:57-58` | operator node outside the unary/binary table (13.x) | R5 W1 | none named | none found |
| B5 | `Build/MirBuilder.Operators.cs:88-91` | sequence member with no BuiltinForm (4.7, 4.6.2, 4.9.5) | R5 W1 | none named | none found |
| B6 | `Build/MirBuilder.Places.cs:49` | Place shape outside the switch (3.4, 15.1) | R5 W1 | none named | none found |
| B7 | `Build/MirBuilder.Places.cs:103-104` | name of another resolution kind (Local/Param/Capture unbound, etc.; 3.4, 11.3) | R5 W1 | none named | none found |
| B8 | `Build/MirBuilder.Places.cs:117` | instance Property storage without a receiver (11.2, 11.3) | R5 W1 | none named (PLAN carried-forward: `S.v` for an instance Property still Unsupported_Kd) | none found |
| B9 | `Build/MirBuilder.Places.cs:201-210` | selection through an identity premise's Type (8.2.3) | BU | PLAN G355 / G168b (TyCtxt identity query with G170, held until C3) | MirBuildTest.Places.AnIdentityPremisesSelectionIsOneUnsupported; ledger `b-identity-places` |
| B10 | `Build/MirBuilder.Places.cs:227-231` | Field of a Place whose Type stores none (identity-premise struct; 8.2.3) | BU | G355 / G168b | MirBuildTest.Places.AnUnsupportedFieldsSiteNamesItsReceiversRoot |
| B11 | `Build/MirBuilder.Selections.cs:148-154` | range selection on an identity premise's Type (8.2.3, 4.6.3) | BU | G355 / G168b | AnIdentityPremisesSelectionIsOneUnsupported (`s[0..1]`) |
| B12 | `Build/MirBuilder.Values.cs:146-147` | value HirOp outside the switch (Binding Error is derived) | R5 W1 | none named | none found |
| B13 | `Build/MirBuilder.Values.cs:190-194` | adaptations "not built yet": StorageProjection, BaseBorrow without one component, Erasure without expected Type (10.2, 7.7.4) | R5 W1 | none named | none found |
| B14 | `Build/MirBuilder.Values.cs:213-216` | aggregate neither a construction nor Tuple/fixed/Array elements (e.g. an Array fill; 4.3.3) | R5 W1 | none named (2x-E1 covered fixed fills) | none found |
| B15 | `Build/MirBuilder.Values.cs:284-286` | Closure capture whose source is unbound in the body (7.7.2) — 2c-7's closure family | R5 W1 | none named (2c-7 registers it for X-2) | none found |
| B16 | `Build/MirBuilder.Values.cs:308-310` | Property assignment with no setter plan (13.7.1, 11.5) | R5 W1 | none named | none found |

### A4. MirEffects (recorder `Analysis/MirEffects.Solve.cs:110-114` at a `confined` obligation; the same `Limit` class reaches A5's E-rows below)

| # | Site (class `Limit` set) | Form (SPEC) | Owner | Removal stage | Pinned |
| --- | --- | --- | --- | --- | --- |
| E1 | `Analysis/MirEffects.cs:124` | compiler function `CompilerFunctionEffect.Unclassified`/Formatting fallthrough (8.11.2) | R4 | none named | none found |
| E2 | `Analysis/MirEffects.cs:273-274` | comparison leaf `ComparisonKind.None` (13.4, 8.11.3) | R4 | none named (a broken contract or a dependent per its comment) | none found |
| E3 | `Analysis/MirEffects.cs:381-385` | Move/Drop item whose Type is null (storage Limit; 4.9.6) | Binding | R7 (G128) | none found |
| E4 | `Analysis/MirEffects.Solve.cs:200` | destroyed/called Type the substitution does not instantiate (G128 cap; 15.6.4) | Binding | R7 (G128) | MirEffectsTest.AChainGrowingThroughADestructorIsALimit |
| E5 | `Analysis/MirEffects.Solve.cs:209-211` | call through an abstract Callable input with several signatures (8.11.4) | R4 | none named (OQ9 item "multi-signature Callable input (2f-2)"; 2f-2 replaced by Abstract Calls Reach by Paths) | MirEffectsTest.SeveralCallableSignaturesAreALimit |
| E6 | `Analysis/MirEffects.Solve.cs:241-242` | Callable value of a Kind other than Function/Closure/Item (8.11.4) | R4 | none named | none found |
| E7 | `Analysis/MirEffects.Solve.cs:299` | requirement on a Type no witness resolves (composite with an abstract part; 8.11.3) | R4 | A-12 (after H11; PLAN row 161), R7 | none found |

### A5. MirOrigins

| # | Site | Form (SPEC) | Owner | Removal stage | Pinned |
| --- | --- | --- | --- | --- | --- |
| O1 | `Analysis/MirOrigins.Chains.cs:66` (-> `:571-575`) | row 9: a row or Ref with an Unclassed end (Closure header/local Function annotation omitted Origin; callee meet of own Origins; I-1 formatting row; 15.3.4, 15.6.4, 15.6.5) | R4 / BU | G216 (b) (R4's queue, after G216 (a); not in the shortened exit), G195 (BU, unscheduled) | MirOriginsTest.AnUnclassedEndIsTheLocatedLimit; ledger `a5-row9`, `a5-row9-incomplete` |

### A5. MirBorrows (recorder `Analysis/MirBorrows.Reports.cs:104`)

| # | Site | Form (SPEC) | Owner | Removal stage | Pinned |
| --- | --- | --- | --- | --- | --- |
| M1 | `Analysis/MirBorrows.Reports.cs:55-58` | every rejection in a PD13-held view: a case admitting a Shared receiver where the call acquires Exclusive (15.8.2, 7.7.3) | R4 | per-case callee acquisition, "PLAN G13" (DECISIONS R4-M126; STATUS cites G13 but **dev's PLAN has no G13 row**) | MirBorrowsTest.Closures.AViewWhoseCaseAdmitsLessThanTheCallAcquiresIsHeld |
| M2 | `Analysis/MirBorrows.Locations.cs:61-64` (marks `Addresses.cs:114`) | PQ-10: Borrow of a `during self` layer's Place, Copy/Move of a Tuple/array enclosing one (5.7, 15.6) | R4 | condition only: "the Loan records the layer's own-step entry and positions" (Addresses.cs:16-17); no G/unit | MirBorrowsTest.Raw.ABorrowOfADuringSelfLayerIsUnsupported; ledger `a5-pq10`, `a5-p13` |
| M3a | `Analysis/MirBorrows.Calls.cs:138` (<- `Reach.cs:281` <- `MirTypes.Holding.cs:143`, `:308`) | W's undecided store past a stored Exclusive raw layer with unpublished members (R4-M147; 5.7, 15.6.4) | BU (fact) | G148 (1) `sharedSupplied` (BU, no stage) | MirBorrowsTest.Guard.AStoreTheGuardLeavesUndecidedIsUnsupported; MirBorrowsTest.Closures.AStoreTheGuardLeavesUndecidedPastAClosureIsUnsupported |
| M3b | `Analysis/MirBorrows.Calls.cs:146-148` (<- `Accesses.cs:86` <- `MirTypes.Destruction.cs:171-174`) | referent store whose referent's destruction meets an abstract part not proven Copy (15.6.4, 16.3.3) | R4 | none named | MirBorrowsTest.DropLiveness.AnAbstractPartBesideAPartPastTheBoundIsUnsupportedAtAReferentStore |
| M3c | `Analysis/MirBorrows.Calls.cs:227-230` | retention pair undecided (`Holds` null) for a non-Value kind (R4-M147; 15.6.4) | R4 | none named | none found (ledger `a5-p13`: "none in the corpus") |
| M4 | `Analysis/MirBorrows.Effects.cs:120-122` | conflict every deciding effect of which lies in a proved initializer (PD12 (ii); 15.6.4, 22.4.3) | R4 | none named (unreachable from source until P42) | MirBorrowsTest.Rooted.AnEnsureWhoseProofWouldDecideIsUnsupportedRooted (hand-built MIR) |
| M5 | `Analysis/MirBorrows.Effects.cs:140` | Loan a write would meet, no key, A4's Limit (8.11.x, 15.6.4) | R4 | follows E1-E7 | MirBorrowsTest.Rooted.ALimitIsUnsupportedAndADerivedEffectReportsNothingRooted (hand-built) |
| M6 | `Analysis/MirBorrows.Effects.cs:155` | R3: Drop of a mutable static with A4's Limit only (16.4, 16.3.3) | R4 | follows E1-E7 | Rooted twins (hand-built) |
| M7 | `Analysis/MirBorrows.Effects.cs:163-165` | R3 with every deciding effect in a proved initializer | R4 | none named | Rooted twins (hand-built) |

### Binding producers whose records A5 relies on (PLAN-named)

| # | Site | Form | Owner | Removal stage | Pinned |
| --- | --- | --- | --- | --- | --- |
| X1 | `src/Kimi/Compiler/Binding/Binding.PlaceAuthority.cs:528-532` (BorrowFailure) | written Borrow of a static-rooted Place (15.6.2, 15.6.4) | BU | P42 (G100: 2b-6 after G:A5-F9; R4-M90 PQ-1) | MirOriginsTest.AnExplicitStaticBorrowIsBindingsLimitUntilP42; MirBorrowsTest.Effects.AnUnsupportedStaticBorrowFormsNoLoan |
| X2 | `src/Kimi/Compiler/Binding/Binding.PlaceAuthority.cs:539-549` (FailStaticBorrow; callers ArgumentOperations.cs:180, CallRelations.cs:226, Enums.cs:543, ValueCalls.cs:360, :485) | implicit Borrow of a static root (receiver, callee, argument, payload) | BU | P42 | MirBorrowsTest.Effects.AQualifiedPartBorrowIsBindingsUnsupported; .AnEnsureWhoseProofWouldDecideIsUnsupported (asserts no A5 record) |
| X3 | `src/Kimi/Compiler/Binding/Binding.Closures.cs:376-379` | unlisted use of a pair-layer capture (R4-M145's net; 2c-7 family; 7.7.3) | BU | none named; R4-M145: "no form reaches it" | none found |
| G124 | none: `MirTypes.HoldsRaw` at `Build/MirBuilder.Places.cs:162`, `MirDescriptors.cs:396` | §8.11.2/§15.2.4 Transferable walk | BU | 2b-4, 2b-5 (not landed: no `TyCtxt.Transferable.cs`, `Kimi.Transferable` undeclared) | — (an unsound acceptance, not an Unsupported) |
| G56 | none: a Binding relation record makes the node a derived Opaque (no record) | 15.6.1, 15.6.5 | BU | H11 | — |
| G216 (a) | none: the boundary's forms (jt1, mj1, … c3a, U3 sites) stay as on dev, a known defect | 15.3.6, 15.6.1 | BU/R4 | root design after R4's exit | — |

## 2. S1015 sections (X-2 list)

| § | Rule changed (72fb4b79) | MIR state |
| --- | --- | --- |
| 8.11.2 | static walk: rc behind base-object/runtime-Contract view, unproven abstract are environment effects | **Neither**: G124 unsound acceptance on both pipelines; raw-static part only (`MirDescriptors.cs:396`; MirEffectsTest.SummariesAreTransitive); removal 2b-4/2b-5 (BU) |
| 14.10.3 | checking state after Never/`$abort`/non-exiting loop | Covered: MirBorrowsTest.LoansFlowOverJoinsLoopsAndCheckOnlyCode, MirDataflowTest.ValuesStayLiveIntoACheckOnlyContinuation, MirInitTest |
| 15.2.4 | buffers/writers/Sender refute Transferable | **Neither**: no Transferable view; a written `T is Transferable` is Binding's UnresolvedBinding_Kd (Language error for a valid form) until 2b-4 |
| 15.3.5 | Phantom slots always invariant | Binding's variance (shared verdict): ClosureResultOriginTest.Arguments `c4_phantom_ctl_bad`; no MIR family test names it |
| 15.3.6 | mixed variance solved as invariant | Binding's solve (CallOriginRelationTest, CallableSignatureInferenceTest); MIR: O1 (row 9) for Unclassed ends; no mixed-variance MIR test found; G216 (a) boundary forms as on dev |
| 15.6.1 | covers object borrows, pair layers; OwnedOrigins | Covered: MirOriginsTest.Proofs.OriginProofFamiliesHaveTheirOutcomeOnTheMir, MirOriginsTest.Unknown.APairLayersWellFormednessIsUnknownOnlyInItsCase, OriginRowsTest; G215 (constructorless rows unjudged) open |
| 15.6.4 (item 1) | input-bounded borrow of mutable static storage | Registered: X1/X2 (Binding, P42); A5 rules on hand-built MIR only (MirBorrowsTest.Rooted) |
| 15.6.4 (item 29) | polymorphic recursion widens a static key | A4: S5 adopted (OQ13 (a), DECISIONS); MirEffectsTest.AGrowingRecursiveDestructionEndsAtTheBound (Deep), E4 (G128 Limit). S5's widening itself not verified here |
| 16.2.1 | temporaries before earlier defers on a transfer | Covered: MirDropTest.AnInterruptedArrayLiteralDestroysItsSecuredElementsInReverse, MirBuildTest.Calls.ADictionaryLiteralsEntriesArePlacedInAValueOfItsOwn |
| 16.3.3 | erased destruction conservatively conflicting | Covered on hand-built MIR: MirBorrowsTest.Rooted.AnErasedDestructionMeetsTheLiveStaticLoansRooted, MirTypesTest.DestructionObservesUserDropsAndUnknownParts; from source the static Borrow is X1/X2 (P42) |

## 3. Counts

- MIR-side producer sites: **33** (B 16, A4 7, O 1, M 9; MirTypes feeders `Destruction.cs:173`, `Holding.cs:143`, `:308` inside M3) plus
  4 recorders; plan said 34 at its base. Binding sites A5 relies on: 3 (X1-X3). PLAN-named non-producers: 3 (G124, G56, G216 (a)).
- With a named removal stage: 11 (B1, B3, B9-B11, E3, E4, E7, O1, X1, X2). Pinned: 18 of 36 (B1, B3, B9-B11, E4, E5, O1, M1-M3b, M4-M7 hand-built, X1, X2); unpinned 18.
- S1015: covered 5 (14.10.3, 15.6.1, 16.2.1, 16.3.3 rooted, 15.6.4 part); Binding-only 2 (15.3.5, 15.3.6); registered 1 (15.6.4 item 1);
  **neither 2 (8.11.2, 15.2.4: G124, an unsound acceptance, not a located Unsupported_Kd).**
- No family-wide gate found: P13's body hold gone (R4-M147), default taint gone (R4-M70 note), A3-closure replaced by M1's per-view hold.

## 4. No-stage list (DEC O4 candidates for OQ9): 25

- Builder catch-alls B2, B4, B5, B6, B7, B8, B12, B13, B14, B16 and the closure capture B15 (11): no known reachable form; the
  census test asserts no construction record in Binding-complete corpus programs; MirBuildTest names only the tuple operator as unbuilt.
- A4: E1, E2, E5 (multi-signature Callable), E6 (4).
- A5: M1 (per-case receivers; G13 cited, no PLAN row), M2 (PQ-10, condition only), M3a (G148 (1), BU, unscheduled), M3b, M3c,
  M4, M7 (and M5/M6 follow E1-E7) (9 counting M5, M6).
- Binding: X3 (1).
- Not Unsupported but unscheduled for R4's exit: G124 (§8.11.2, §15.2.4; 2b-4/2b-5 not landed).

## 5. Findings

- Unreachable from source until P42: M4-M7 (Binding fails every static Borrow, so A5 holds no Loan on a static); pinned only on
  hand-built MIR (MirBorrowsTest.Rooted). X3 by R4-M145's own note; M3c "none in the corpus".
- Unpinned: B2, B4-B8, B12-B16, E1-E3, E6, E7, M3c, X3.
- Stale records: PLAN G128 says A5's retention Limit is "a located Unsupported_Kd (R4-M51)"; since R4-M153 it is
  OwnershipDepthLimit_Kd (`MirBorrows.Locations.cs:70`). STATUS cites "PLAN G13" with no G13 row on dev. G124's row says
  "until then a located Unsupported_Kd on the MIR", but dev reports nothing (accepts).
