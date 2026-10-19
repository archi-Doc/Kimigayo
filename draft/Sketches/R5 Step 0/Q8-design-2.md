# Q8 design 2: the R6-check prerequisite package

Date: 2026-10-18. Base: `origin/dev` at `147c4616`. Second independent design of the R5/R6 plan's unit Q8 (§4.8); written
without reading `deep-dives/q5-r6check-prereqs.md` or any other design of Q8. Inputs: the plan (§1–§3, §4.8, §4.10, §5, §7),
DEC R4-M1, R4-M3a, R4-M4, R4-M17, R4-M35, R4-M61, R5-M1–R5-M13, R2b-D2, PLAN10 §5.1, §5.5, §7, §9, the R4 branch
`claude/a5-1b-core` (MirReport facts), the open-findings note (Q13), and the code cited below. "(H)" marks an inference not
checked by running code. Line numbers are at the base unless a branch is named.

Governs: Q7, the lint group G:L (Q9–Q12), Q16's Binding views, Q17, H1 (with PD29, PD31, PD32), the dispositions, and two
new units this design adds (H1a, and the closure of Q13). Approved once by the owner after its design review (OQ10, R5-M1).

## 0. Decisions in one page

| # | Decision | Section |
| --- | --- | --- |
| D1 | `OwnershipRequirement` (18 names, same order and values) in `Diagnostics/DiagnosticRequirement.cs` replaces `OwnershipFailure`; the old analysis uses it as its failure kind, as Binding uses `BindingFailure` | §2.1 |
| D2 | One Binding component, **HIR lints** (`Binding.Lints.cs`): a 12-row lint table (code, condition, derivation class, priority), one record list, one report. 11 rows are filled by one Final walk or at the jump and label bindings; FunctionFallthrough by the end-arrival check of result validation (PD32). `UnsafeFunctionValue` gets no row: Binding already fails it at four sites, so the old analysis's copy is deleted | §2.2–§2.4 |
| D3 | PD31's record form, made precise: requirement `Binding` (Kind 0), condition = the lint row's number, non-failing, built at Final, repairs built at report time; the old analysis reads a Binding fact (`LintErrors`) where it read `flow.Issues` | §2.2 |
| D4 | Binding owns jump targets: the jump binding writes the target into the jump's `PlanRow.Plan` (R7's inline shape); the publisher stops resolving it; the HIR contract "a Jump or Continue whose published target is null was reported by Binding" lets the MIR builder treat it as derived (ask A-8) | §2.3 |
| D5 | Q16: two Binding views, `BindingSymbol.Mutable` and the Tuple-element position as `PlanRow.Folded`; a third (constructor receiver) only if no signature fact answers it | §2.5 |
| D6 | **Departure**: Q17 does not add a code → requirement table that `MirReport` disambiguates. The check that records a MIR problem names its requirement (`MirProblem.Requirement`), as Binding's obligation row already names its code; the ledger compares the requirement column. Supersedes R4-M3a's derivation clause (its reason, Mir unable to name `OwnershipFailure`, ends with D1) | §2.6 |
| D7 | H1 judges TransferRequired by **reading the published Adjust column** (the findings' option (a)), after a whole-pass publication inside the Final pass, as a linear scan; a failed node triggers one republication. `Consume` is not restructured (option (b) rejected). Node failure with the existing `BindingFailure.TransferRequired` | §2.7 |
| D8 | H1's subject set has three sources, not one: consumed children (Adjust), **discarded Places** (Discard Context items the column never records) and **inferred captures** (whose acquisition Binding never sets). Both extra sources are soundness gaps of the MIR path today (H, Q14 confirms) | §2.7 |
| D9 | New unit H1a (behavior-preserving): split `Binding.Check` into failure producers and counting, and publish HIR eagerly at the end of each Final pass (refines R2b-D2). Q13 closes as found (no extraction needed); Q14's prerequisite becomes Q8 | §5 |
| D10 | `Binding.ClosureInstances` and `Binding.RequirementInstances` are re-disposed from "delete R6" to R7 | §2.8 |

## 1. Required meaning

| Rule | Forms it governs | Today's judge |
| --- | --- | --- |
| SPEC 17.5 (priority: Unit inference > Result > try success > effect-free), 17.5.1–17.5.3, 23.1.3 repairs `ExplicitDiscard`, `PropagateFailure` | `DiscardedTail`, `DiscardedResult`, `UnusedTrySuccess`, `DiscardedValue` at Discard Context occurrences | `ControlFlowAnalysis.cs:641-656, 1500-1506, 526-576` |
| SPEC 17.5 ¶3, 22.10.4 | `OwningWriteArgument` at an interpolated literal passed to `Utf8Writer.write` | `ControlFlowAnalysis.cs:895-904` |
| SPEC 14.9.3 last ¶ | `StaticWhileTrue` | `ControlFlowAnalysis.cs:1449-1452` |
| SPEC 14.3, 5, 4.3.4, 23.1.3 `RemoveUnsafe` | `UnsafeBlockRequired`, `UnnecessaryUnsafeBlock` | `ControlFlowAnalysis.cs:679-688, 849-863, 1085-1123` |
| SPEC 7.6 | `UnsafeFunctionValue` | Binding node failure at four sites (`Binding.Expressions.cs:881`, `Binding.FunctionItems.cs:394`, `Binding.FunctionTypes.cs:518`, `Binding.RequirementItems.cs:86`) and again in `ControlFlowAnalysis.cs:658-677` |
| SPEC 14.5.2 (missing or wrong-kind target; unlabeled `yield` to a selection in Discard Context), 14.4 (overlapping labels) | `InvalidJumpTarget`, `UnlabeledYieldTarget`, `OverlappingLabel` | `ControlFlowAnalysis.cs:1279-1301, 1511-1526` |
| SPEC 14.9.1 (indented body: Unit on structural end arrival), 14.9.2 step 4, 14.9.3 | `FunctionFallthrough`: a Block body of a function or accessor whose result is not Unit reaches its end | `ControlFlowAnalysis.cs:1230-1252` |
| SPEC 3.5, 15.1.5 lending rule, 14.2.2 ("Discarding destroys the result at its normal lifetime"), 14.2.4, 7.7.2 (no list: each capture must be Copy) | `TransferRequired`: a bare Place is never moved | Binding for calls, conversions, payloads, value calls and explicit capture entries; the old analysis at 10 sites for the rest (§2.7) |
| SPEC 23.1.2 | Each independent failed requirement is reported; dependents are implementation-defined | all |
| DIAGNOSTICS rule 1 (`DIAGNOSTICS.md:22`), §4.3 "Later phases" (`:106`) | A requirement is judged by the phase of its partition; a later check resting on Binding's failure is derived | all |

## 2. Components

### 2.1 Q7: `OwnershipRequirement`

| Item | Content |
| --- | --- |
| Responsibility | The requirement kinds of the Ownership partition as Diagnostics vocabulary; their stable names already live in `DiagnosticRequirement.tinyhand:590-641` |
| Representation | `public enum OwnershipRequirement : byte` with the 18 members of `OwnershipFailure` (`Analysis/OwnershipModel.cs:139-172`) in the same order, so every Kind value, name and record stays equal (bp); `DiagnosticRequirement.Ownership(OwnershipRequirement)` (`DiagnosticRequirement.cs:41`), `All` (`:91-94`) and `NameOf` (`:110`) switch to it |
| Changes | `OwnershipFailure` deleted; `OwnershipIssue.Failure` is an `OwnershipRequirement`; 85 src and 51 test references renamed (grep at base) |
| Invariants | One enumeration per partition's kinds (Binding: `BindingFailure`; Ownership: `OwnershipRequirement`); names equal to the tinyhand table's entries (the existing load-time consistency check, `DiagnosticRequirement.cs:151-209`) |
| Prohibitions | No second (Analysis-side) copy and no mapping switch; no member added in R5/R6 except by owner (PD21 analogue) |
| Later | H1 removes `TransferRequired` (§2.7); H10/H11 remove members only the old analysis produced, each in the unit that deletes its last producer; R7's Problem records may unify the per-partition enums (PLAN10 §7) |

Rejected: a parallel `OwnershipRequirement` with a mapping from `OwnershipFailure` (a second copy); moving `OwnershipFailure`
unchanged (the name describes the old analysis, not the requirement); one cross-partition requirement enum now (that is R7's
`BindingFailure` replacement, PLAN10 §7).

### 2.2 HIR lints (Q9, Q10)

**Contract** (entry file `C/Binding/Binding.Lints.cs`):

| Item | Content |
| --- | --- |
| Responsibility | The diagnostics of P5 (ARCH target pipeline row "HIR lints"): the 12 rows below, recorded once per Final pass as non-failing Binding records, with their repairs and their derivation from Binding failures and parser recoveries |
| Inputs | The Final pass's bound tree and facts: Types, adaptations, call plans, Copy proofs, `InfersUnit`, structural completion (`ResultStructure`), recovery causes |
| Outputs | `lints`, a pooled `List<(Koto Node, byte Row, Koto? Root)>` cleared in `ResetPass`; `LintErrors` (count of rows whose catalog severity is Error); records published by `ReportDiagnostics` at Final beside the alias, pattern and position warnings (`Binding.cs:145-159`) |
| Invariants | Never fails a node, so `Binding.Result` and every later phase see the same completeness as without lints; one row per code; condition = row number; a derived Error uses the same requirement and condition; warm rebind 0 B (no Koto-keyed set or dictionary: duplicates and priority are resolved by sorting `lints` by (SyntaxId, priority) at report time) |
| Prohibitions | No inference or proof re-run to explain a record; no lint of library bodies (source modules only, as today `OwnershipAnalysis.cs:84-87`); no record from a tentative binding reaches the report (the walk runs at Final; site-recorded rows are idempotent and merged by problem key) |

**The lint table** (replaces `ControlFlowAnalysis.ReadsBinding`, `:275-277`, the priorities `:450-467` and the ad-hoc
`GuessedBy` code list `:392`):

| Row | Code | Sev. | Recorded at | Reads Binding (derived from `FailureCauses`; a warning records nothing) | Rests on a guess (supplied body or, D2, unresolved node) | 17.5 priority |
| ---: | --- | --- | --- | --- | --- | ---: |
| 1 | `DiscardedTail_Kd` | W | walk, at a `do`/`if`/`match` whose Unit Binding inferred | yes | yes (descent root) | 1 |
| 2 | `DiscardedResult_Kd` | W | walk, Discard Context occurrence | yes | no | 2 |
| 3 | `UnusedTrySuccess_Kd` | W | walk | yes | no | 3 |
| 4 | `DiscardedValue_Kd` | W | walk | yes | no | 4 |
| 5 | `OwningWriteArgument_Kd` | W | walk, `WriterWrite` call | yes | no | — |
| 6 | `StaticWhileTrue_Kd` | W | walk | no | no | — |
| 7 | `UnsafeBlockRequired_Kd` | E | walk | no | no | — |
| 8 | `UnnecessaryUnsafeBlock_Kd` | W | walk, at the block's end | yes (unknown need counts as use) | no | — |
| 9 | `InvalidJumpTarget_Kd` | E | jump binding (§2.3) | no | yes (unlabeled jump's target) | — |
| 10 | `UnlabeledYieldTarget_Kd` | E | jump binding | no | yes | — |
| 11 | `OverlappingLabel_Kd` | E | label binding (`Binding.Expressions.cs:767`) | no | yes (label) | — |
| 12 | `FunctionFallthrough_Kd` | E | end-arrival check (§2.4) | no | yes | — |

`UnsafeFunctionValue_Kd` has no row: Binding fails it as a node (`BindingFailure.UnsafeFunctionValue`), and the old copy
already became derived where Binding failed the node (DIAGNOSTICS `:106`, "a later phase repeating Binding's own check").
Q10 proves with a reproducer that the four Binding sites cover every form `ControlFlowAnalysis.cs:658-677` reports, then
deletes that copy; a form not covered becomes a fifth Binding node failure at its binding site, not a lint row.

**Record form (PD31 refined).** `node.Report(DiagnosticRequirement.Binding(BindingFailure.None), code, …, condition: row)`.
`Koto.ReportDerived` (`Parsing/Koto/Koto.cs:665-666`) gains the optional `condition` that `Report` already has, so a derived
lint Error keeps its check key distinct from the node's own Binding failure (requirement Kind ≠ 0) and from another lint
row at the same node. The code's catalog severity decides Error or Warning; acceptance reads the Binding partition's error
state as today (`Project.cs:510`), so a lint Error rejects without making Binding incomplete.

**Walk.** One `ForEachChildSlot` walk of the source modules after `Check(Final)`'s counting (`Binding.cs:115`), visiting
evaluated syntax only (Types, attributes, Origin clauses, effect items, constraints skipped as in
`ControlFlowAnalysis.cs:761-830`), excluded tests skipped (`TestDefinition`), a body with an unselected directive group
treated as today (`HasInvalidDirective`, `:1255-1274`). It keeps two pieces of walk state: the innermost Unsafe Block's
used/uncertain bits (`:849-863`) and the function boundary. `IsDiscarded(node)` (the Discard Context test at `:641`) becomes a
Binding predicate, reused by H1 (§2.7).

**Transitional reads of the old analysis** (deleted at H10):

| Old read | After |
| --- | --- |
| `earlierRejected = flow.Issues.Count != 0 \|\| …` (`OwnershipAnalysis.cs:101`) | `… \|\| binding.LintErrors != 0` (from Q10, the first unit moving an Error row) |
| `verified = flow.Issues.Count == 0 && …` (`:116`) | same addition; acceptance is unchanged either way (`Project.cs:510`) |
| `flow.Reported(body, FunctionFallthrough_Kd)` (`:263`) | `binding.FallthroughKey(function)` (Q12) |
| `LegacyRecords` reads `ControlFlow.Issues` (`tests/xUnitTest/LegacyRecords.cs:28-31`) | also reads Binding's lint Errors as Binding records (the ledger's common side, R4-M45 `Sides`) |
| `ControlFlowAnalysis.Targets` (`OwnershipAnalysis.cs:1966`) | unchanged until H10: CFA calls the same pure rule; editing frozen code buys nothing (D5 freeze) |

The `ControlFlow` requirement then names only the old remainder and MIR construction, validator and A3 records; Q10 updates
its description ("the control flow is valid") in the tinyhand table.

### 2.3 Binding-owned jump targets (Q11)

| Item | Content |
| --- | --- |
| Rule | SPEC 14.5.2's lexical lookup stays the one function `KotoHelper.ResolveTransferTarget` (`Parsing/Koto/Common/ControlFlowContext.cs:13`) |
| Owner of the fact | The jump binding (`Binding.Expressions.cs:716-717`), which already resolves the target for result typing, writes `HirTables.PlanFor(jump, target is not null).Plan = target` (R7's inline write) and records rows 9–10; the label binding records row 11 (the parent scan of `ControlFlowAnalysis.cs:1511-1526`) |
| Publisher | Deletes its own resolution (`Binding.Hir.cs:98-102`), which violates its header "Must not resolve again" (`:6`) |
| HIR contract (ARCH "Binding facts for MIR" row) | "A published Jump or Continue has its target row; a null target means Binding reported `InvalidJumpTarget_Kd` there." |
| MIR consumer (ask A-8, R4's G103-E9b jump part) | The builder's published-fact `Rejected` (`Mir/Build/MirBuilder.cs:70-71`) adds "a Jump or Continue whose target row is null", so `Transfer`'s `Unsupported(jump)` (`MirBuilder.Control.cs:116`) becomes derived with no record. The jump's operand is still built first, so its independent errors stay (SPEC 23.1.2); "not built yet" targets (for, match, defer) keep their located `Unsupported_Kd` |
| Other readers | Binding-internal readers that run before the jump is bound (`Binding.ControlFlow.cs:490`, `StructuralCompletion.cs:262`, `Binding.ArgumentOperations.cs:395`, `Binding.Try.cs:20`, `Binding.Testing.cs:31`, `Binding.OriginDiagnostics.cs:151`) keep calling the pure rule; post-Binding readers read the row |

Rejected: publishing a target-less jump as `HirOp.Error` (its operand would be Replaced, losing the operand's independent
errors on the MIR side); a node failure (Binding incomplete, so the old analysis skips the program until H5: the regression
PD31 avoids); a new HIR column for targets (the plan row slot already holds it).

### 2.4 FunctionFallthrough in Binding (Q12, PD32)

- The check is result validation, so its function lives beside `AddBodyResult` in `Binding.ControlFlow.cs` (`:517-531`):
  `EndArrival(owner)` for a function or accessor with a Block body whose declared result (written result, else bound
  contract, Unit for a discarding body or setter, as `ControlFlowAnalysis.cs:285-294`) is not Unit: if `ResultStructure()`
  says the body can complete, the end-arrival Unit source cannot fit, which is row 12 at the body.
- It runs once per function and accessor from the Final walk, not inside `BindFunction` (`Binding.Functions.cs:92-95`),
  `BindAccessorBody` and `BindClosure`: one call site instead of three, and no record from a tentative pass.
- **One guess rule.** CFA has two gates: a pending body (an unresolved node) suppresses the record (`:1248`), and a completion
  through supplied bodies makes it derived (`:379-424`). Binding generalizes the `isGuess` predicate of `StructuralCompletion`
  (`StructuralCompletion.cs:16-17`) to "a node whose completion Binding did not establish": a supplied body (its recovery key)
  or an unresolved expression (`Unknown`, `ControlFlowAnalysis.cs:281-282`, its `FailureCauses`). If the body completes only
  through guesses, the record is derived from their keys; the visible result equals today's (a derived record whose causes
  are published is suppressed). Row 1's descent uses the same rule.
- `FallthroughKey(function)` returns the record's check key, read by the old derived link (`OwnershipAnalysis.cs:262-267`)
  until H10. On the MIR side, A1's `UninitializedPlace_Kd` at the return delivery of such a body is the same dependent; H0
  decides whether A6 derives it from this key or the builder builds that end arrival as a derived Opaque value (open
  question §8).

### 2.5 Q16: Binding views for the MIR builder's syntax-class sites

R5-M10 leaves 17 sites that choose an operation from syntax. Existing facts answer most (`HirOp.Continue` for
`MirBuilder.Control.cs:121`; `HirOp.Closure` or `BoundClosure.Of` for `MirBuilder.cs:201, 459`, `MirBuilder.Places.cs:131`,
`MirBuilder.Defaults.cs:341`; Type kind for `MirBuilder.Values.cs:300`; `HirOp.Group` for `MirBuilder.Places.cs:27`). The rest
need these views:

| View | Contract | Owner | Replaces |
| --- | --- | --- | --- |
| V1 `BindingSymbol.Mutable` | Whether a local, Pattern binding, iteration slot or static Property binding is reassignable (SPEC 6.6, 14.6.1, 14.8.2), set once where the symbol is declared from `Binding.IsMutableDeclaration` (`Binding.Expressions.cs:10-12`) and a `var` static Property | Binding | `MirBuilder.Match.cs:167`, `MirBuilder.Expansions.cs:109`, `MirBuilder.Places.cs:161` |
| V2 Tuple element position | The publisher writes `PlanRow.Folded` on a Tuple-element Field node, as it does for a ConstIndex position (`Binding.Hir.cs:112-116`) | Binding publisher | `MirBuilder.Places.cs:168` |
| V3 (conditional) constructor receiver | Only if neither `CallPlan.Parameter(function, -1)` nor the signature tells a constructor from a destructor at `MirBuilder.cs:163`; then a `BindingSymbol` fact. Q16 decides by reading the signature first | Binding | `MirBuilder.cs:163` |

Each view is added to `PublishedBindingTypes` with its reason (R4-M9 pattern); 0 B warm (fields set during the pass).

### 2.6 Q17: the requirement of a MIR problem (departure)

The plan's Q17 puts a code → requirement table in Diagnostics and lets `MirReport` disambiguate by facts. That re-derives,
at a consumer, a decision the recording check already made, and it reverses PLAN10 §7's "requirements are held independently
of codes; never reverse-looked-up from the code". The reason R4-M3a gave for leaving the requirement out of `MirProblem`
("Mir may not name the legacy `OwnershipFailure`") ends with Q7.

| Item | Content |
| --- | --- |
| Representation | `MirProblem(DiagnosticCode Code, DiagnosticRequirement Requirement, SourceRef Subject, ulong Views)` (`Mir/Analysis/MirReport.cs:22`; +4 bytes); the merge key stays (Code, Subject) (R4-M3a), with the invariant that one (code, subject) has one requirement |
| Who names it | The rule that records: A1 (`MirInit.cs:167, 215, 222, 257`) its `Ownership.*` kind; A2 (`MirDrops.cs:293`) `Ownership.PossiblyMovedUse`; A3 (`MirLints.cs:38`) `ControlFlow`; A4's obligation row, which already carries its code from Binding (`Binding.EffectObligations.cs:14-15`, `MirEffects.Solve.cs:102-112`), carries its requirement beside it (witness → `Ownership.EffectBound`, virtual → `Ownership.VirtualEffectBound`, Callable → `Ownership.CallableEffectBound`); A5 rules (about 6 sites on `claude/a5-1b-core`) theirs; `Unsupported_Kd` from A4/A5 `Ownership.Unsupported`; construction records and validator findings `ControlFlow` when H3 folds them in |
| Old `Binding.*` names | The old path reports some A4/A5 decisions under Binding requirements (`MirDifferenceLedger.cs:56-58, 69-70`). MIR publishes the Ownership name (DIAGNOSTICS rule 1: the deciding phase's partition); the ledger classifies the requirement difference (d) with that clause |
| Ledger | The MIR side's requirement column is the problem's requirement name, not "-" (R4-M17's raw row); a paired old and new row with different requirements is a `requirement` difference kind, classified like the others |
| Consistency | No runtime table. `MirDifferenceLedger.Compared` (`:45-74`) gains the MIR name per old row; a test asserts every MIR problem's (code, requirement) appears there. Deleted with the ledger at H10, after which snapshots pin the names |
| Owners | `MirProblem`, `MirReport` and every recording site are R4's (ask A-5, rewritten: "each recording rule names its requirement"); the ledger column is Q17's (hold adv) |

If R4 declines A-5 in this form, the plan's form is the fallback: a code → requirement table in Diagnostics with a
selector column for the three ambiguous codes (`UnsatisfiedEffectBound_Kd`, `IncompatibleContractImplementation_Kd`,
`Unsupported_Kd`), which needs A4's obligation kind as a `MirFact` row (absent from R4's fact kinds `Anchor`, `Use`,
`Store`, `Reservation` on `claude/a5-1b-core`). That is an owner question (§8).

### 2.7 H1: TransferRequired in Binding

**Rule.** A bare Place is never moved (SPEC 3.5, 15.1.5). H1 rejects every acquisition the MIR builder would build as a
`Move` of a written Place that the program did not write as `@move`.

**Why it is soundness-critical.** The builder's only `Move` of a Place operand comes from the Adjust column: `Acquire`'s
default branch (`Mir/Build/MirBuilder.Values.cs:172-181`). A1 checks only static Move Paths (`MirInit.cs:162-168`). So today
MIR accepts any non-written Move the column records, and the ledger cannot see it because TransferRequired passes through
uncompared (R4-M4, `MirDifferenceLedger.cs:41-44`).

**Subject set** (D8). Three sources; the first is the plan's, the other two are gaps found here (H; Q14 confirms):

| Source | What the column or plan records | Old judge | H1 check |
| --- | --- | --- | --- |
| S1 consumed children | `Adjust = (Move, _)` written by `Take`'s bare rule (`Binding.Hir.Adjust.cs:169-199`) or the group-erasure path (`:39`) | 9 of the 10 sites | `Op` is a value Op (not `Syntax`, so Pattern and Covered positions are out; not `Error`, so nodes Binding already failed are out), the node is a written Place (`IsBarePlace`, a raw Place, a static name; not a formatting plan's storage), and its parent is not `ConversionKoto { ConversionBinding: Transfer }` (the only writer of an explicit Move, `:42`) |
| S2 discarded Places | nothing: `Consume` takes only a value-context result (`:31-33`, `:162`), so a Discard Context item keeps `Unset`, and the builder reads it as "by case" (`MirBuilder.Values.cs:105-108`), which Copies a non-exclusive Type even when it is not Copy | the local and element sites (`OwnershipAnalysis.cs:881-928`: a statement item is `Expression(node)`, a Consume without acquisition, `:930`, so `:836` fires) | `IsDiscarded(node)` (§2.2), a written Place, `Adjust` unset, and `BareAcquire(type) == Move` with a proof other than Error |
| S3 inferred captures | nothing: an omitted capture list never sets `CaptureAcquisition` (`Binding.Closures.cs:473-499`), whose default is `Copy` (`BindingModel.cs:80`) and whose pooled symbol is reused across passes without reset | `OwnershipAnalysis.Closures.cs:46-62` | at closure binding: SPEC 7.7.2 "no list: each must be Copy": a capture whose Type is not proven Copy fails the closure with `TransferRequired` at condition = capture ordinal (the explicit-entry form, `Binding.Closures.cs:209-242`); a proven one sets `Copy` explicitly |

Binding's existing TransferRequired failures (`Binding.ArgumentOperations.cs:926`, `Binding.Conversions.cs:269`,
`Binding.Enums.cs:477`, `Binding.ExplicitAdaptations.cs:269`, `Binding.ObjectViews.cs:219`, `Binding.ValueCalls.cs:417`,
`Binding.Closures.cs:523`) stay. They fail the consumer or the source node, so the published column already shows the node
as `Error` or Replaced and S1 never judges it twice.

**Option choice** (findings: `HirPublisher.Consume`, `Binding.Hir.Adjust.cs:19-159`, writes 2,620 children twice, 480 with
different values; last write wins):

| | (a) Read the published column | (b) Restructure `Consume` so each child is taken once |
| --- | --- | --- |
| What it reads | The value the builder reads, after every write | A new enumeration, which must equal the column |
| Duplicates | Irrelevant: the scan runs after publication, so the deepest consumer's (last) write is what it sees | Must be removed per family (for iterables, `$tryWrite` writers, index assignment targets) and proven equal |
| Representation change | None to the column; publication moves into the Final pass (H1a) | A new contract for `Consume`, a design review, HIR snapshot churn |
| Size | ≈ +60 (scan) | ≈ +150/−100 (H) before H1 itself |
| Soundness | Exactly what MIR builds is judged (argument below) | The same only if the new enumeration and the column agree everywhere |

(a) is chosen. The duplicate writes are recorded as an open issue (publication speed), not an H1 prerequisite.

**Placement and timing.** A node failure must exist before `Check` counts states and collects issues (`Binding.cs:1006-…`),
but publication today runs lazily after the pass (`Binding.Hir.cs:35-51`, R2b-D2). H1a therefore splits `Check(mode)` into
its failure producers (definition obligations, OCC paths, call and runtime-test constraint checks, `Unvisited` →
`Unresolved`) and its counting, and publishes HIR between them in a Final pass. H1 adds `CheckTransfers()` after that
publication: a linear scan of each module's `Ops`, `Adjusts` and `Syntax.Nodes` arrays applying S1 and S2. If it failed a node,
the modules are published once more (failed nodes become `Error`, their subtrees Replaced, `Broken` recomputed); an accepted
program pays the scan only. `PublishHir()` then returns the pass's version without work.

**Soundness argument.**

1. Completeness: every `Move` of a written Place in MIR comes from (i) the Adjust column, (ii) a capture's acquisition, (iii) a
   Pattern acquisition from Binding's match plan, (iv) `@move`. H1 rejects (i) unless written; S3 makes (ii) `Copy`,
   `Reborrow`/borrow from an explicit entry, `Move` only from `x@move`, or rejected; (iii) and (iv) are written or
   plan-judged. S2 closes the one path where the builder decides an acquisition without the column: after H1 every accepted
   discarded Place has a bare acquisition of Copy, Reborrow or by case, which is what `MirBuilder.Values.cs:105-108` builds.
   Hence an accepted program has no unwritten Move of a Place (plan §7 finding 2 closed).
2. Agreement: the scan reads the same row the builder reads, of the same pass, after every write.
3. No dependents: a failed node is published `Error`, so the builder builds its derived Opaque value (`MirBuilder.cs:409-434`),
   A1 sees no Move and reports no `StaticMovePathRequired_Kd` or moved-use dependent (ARCH R6 row: "makes A1's
   StaticMovePathRequired at the bare acquisitions it rejects derived", by construction rather than suppression).
4. Independent errors: after H5 the MIR analyses run on an incomplete Binding (Opaque at failed nodes), so other errors stay
   (SPEC 23.1.2). Before H5 the old analysis would skip the program (`OwnershipAnalysis.cs:89-92`), which is why H1 merges with
   H5 (PD29).
5. Pending: an `Unset` row from an Error Copy proof (`Binding.Hir.Adjust.cs:198`) is not judged (derived from that Error).

**Precision.** A column row that says Move where SPEC says borrow would be a builder miscompile as well; H1 turns it into a
rejection Q14 finds before H1 lands, and the fix goes into the column's owner (the publisher's consumption rule), never into
H1. Q14's probe (no commit) runs S1–S3 read-only after `PublishHir()` over the test corpus, the diagnostic corpus, the library
and the 40 harnessed milestones, and compares the (code, subject) sets with the old analysis's TransferRequired records and
Binding's existing ones: every difference is classified (a) old miss, (b) column defect (fixed first), or (d).

**Records and deletions.** Report through the existing `FailAcquisition` path (`Binding.Diagnostics.cs:1253-1270`): located
at the Place, Take judged for the repair as the old analysis does (`OwnershipAnalysis.cs:271-272`). H1 deletes the 10 old
report sites (`OwnershipAnalysis.cs:836, 1217`, `.Borrows.cs:447`, `.Closures.cs:60`, `.Elements.cs:253`, `.Objects.cs:27`,
`.Pointers.cs:87, 302`, `.Sequences.cs:65, 109`) and `OwnershipRequirement.TransferRequired` (Kind values after it shift; only
names are published).

### 2.8 Dispositions

| File | R0 disposition (`artifacts/verify/reduction-R0/disposition.md:106, 191`) | New | Reason |
| --- | --- | --- | --- |
| `Binding/Binding.ClosureInstances.cs` (85) | delete at R6 | R7 | `ClosureSignature` has 9 Binding callers (`Binding.CallableProofs.cs:96`, `Binding.Constraints.cs:246`, `Binding.Diagnostics.cs:251, 819, 842`, `Binding.EffectBounds.cs:354`, `Binding.Expressions.cs:396`, `Binding.ValueCalls.cs:276`; `Binding.Structs.cs:34` for `CloseClosureTypes`). K2 deletes the never-cleared context list; the rest becomes `TyCtxt` views in R7's Types consolidation |
| `Binding/Binding.RequirementInstances.cs` (198) | delete at R6 | R7 | `Witnesses` serves A4 through `TyCtxt.Effects.cs:40`; `InstantiateRequirementCall` is the bind-time CallPlan materializer (`Binding.SelectedRequirements.cs:43`, `Binding.RequirementItems.cs:131`, `Binding.Specializations.cs:164`); F5a extracts the witness resolver; R7's ContractSolver and CallPlan pipeline replace it; only `InstantiateForwardedCall` goes at H11 |

Both stay PD20 hold files (R5-M7). The plan §7 finding on the R2a Bind cost is already recorded by R5-M9.

## 3. The PDs this package covers

| PD | Position | Detail |
| --- | --- | --- |
| PD29 | Agree; refine | H1 (node failures) stays in G:R6-check-flip and merges with H5. H1a (bp) is not part of the flip: it may merge any time after G2, when every Analyze already publishes HIR, so eager publication adds no cost on the check path |
| PD31 | Agree; refine | Reuse confirmed sufficient: no new `BindingFailure` (PD21). Refinements: 11 of the 13 codes are lint rows; `UnsafeFunctionValue` is already a Binding node failure (its CFA copy is deleted); FunctionFallthrough is row 12 recorded by result validation; condition = row number; `ReportDerived` gains a condition; repairs at report time; the old analysis reads `LintErrors` |
| PD32 | Agree | Placement: `EndArrival` beside `AddBodyResult`, run once per function and accessor at Final; one guess rule for supplied bodies and unresolved nodes. The DEC entry (Q12) names the departure from PLAN10 §5.5/§9 and its reason (a typing check on the published Completion's structural rule; a second completion rule in A3 avoided) |
| PD20 | Agree | Q7 and Q17 take adv hold lines on `DiagnosticRequirement.cs` and `MirDifferenceLedger.cs`; Q16 in W1 |
| PD21 | Agree | No vocabulary entry anywhere in this package |

## 4. Departures from owner-approved and recorded texts

| Text | Departure | Reason | Cost |
| --- | --- | --- | --- |
| PLAN10 §5.5 A3 row and §9 (`ControlFlowAnalysis` reachability → A3) | FunctionFallthrough judged by Binding (PD32, already in the plan) | SPEC 14.9 makes it result validation by Structural Completion | none beyond the plan |
| R4-M3a "No Requirement … R6 unit a derives the published requirement from the code and the facts" | MIR problems carry the requirement their check names (D6) | Q7 removes the stated reason; a consumer re-deriving a check's identity is a symptom fix (AGENTS); PLAN10 §7 already said requirements are not looked up from codes | R4 changes `MirProblem` and ≈ 12 recording sites (A-5); +4 bytes per problem |
| R4-M3a "Mir may not name `OwnershipFailure` … would make old-path vocabulary shared" | Diagnostics owns `OwnershipRequirement` (Q7, already in the plan) | The names are public diagnostic vocabulary in the tinyhand table, not old-path concepts | none |
| R2b-D2 "The HIR publisher runs lazily, at most once per Binding pass" | Eager at the end of each Final pass, twice when H1 failed a node (H1a, H1) | H1 judges the published column inside the pass; the direction is R7's inline writes | Bind-only callers (tests, hover without analysis) pay one publication per Final pass; measured in H1a |
| R4-M4 (TransferRequired passes through) | Ends at H1 | TransferRequired becomes Binding's, published by both paths | none |
| R0 disposition rows (§2.8) | R7 instead of R6 | definition-time callers | none |
| The plan's Q13, Q14 and Q17 rows | Q13 closed; Q14 depends on Q8; Q17 has no Diagnostics table | §2.6, §2.7 | none |

## 5. Unit mapping

| Unit | Change | Prereq | Verification | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- |
| Q7 | §2.1; DEC entry superseding R4-M3a's naming clause | Q8, OQ10 | as plan; bp (Kind values equal) | ±30 (+136 renamed) | D+R (Q8) | adv |
| Q9 | `Binding.Lints.cs` contract, table, records, report, derived rules; rows 1–6; `IsDiscarded`; tests observe diagnostics; CFA rows 1–6 removed; DIAGNOSTICS "Later phases"; ARCH canonical row "HIR lints"; DEC entry (component, PD31 form) | Q6, Q8, OQ10 | as plan; snapshot classified (requirement ControlFlow → Binding only); Bind and Analyze timing; warm Bind 0 B (`Purpose=Allocation`) | +280/−230 | G:L | no |
| Q10 | Rows 7–8 with `RemoveUnsafe`; `UnsafeFunctionValue` reproducer, CFA copy deleted; `LintErrors` and the two old-analysis reads; `LegacyRecords`; ControlFlow description | Q9 | as plan; reproducer of each CFA `UnsafeFunctionValue` form | +120/−150 | G:L | no |
| Q11 | §2.3: target row at the jump binding, rows 9–11, publisher resolution deleted, ARCH HIR-contract sentence; DEC entry | Q9 | as plan; `HirColumnsTest` target rows; a target-less jump has a null row | +40/−45 | G:L | no |
| Q12 | §2.4: `EndArrival`, guess rule in `StructuralCompletion`, `FallthroughKey`, old derived link re-pointed; DEC entry (PD32) | Q9 | as plan; `syntax.json` `guessed-*`/`written-*` rows equal | +45/−30 | G:L | no |
| Q13 | Closed: `Consume` already is the one enumeration with `Take` the one rule (findings) | — | — | 0 | — | — |
| Q14 | Probe (§2.7) with S1–S3 and the pre-order of writes, evidence `artifacts/verify/r6-q14/` | Q8 | Evid | 0 | — | no |
| Q16 | §2.5 views plus the 17 sites | F0 (W1), OQ10, OQ14 | as plan; bp | +35/−40 | D+R (Q8) | W1 |
| Q17 | §2.6 ledger column, `requirement` difference kind, `Compared` MIR names, the consistency test; DEC entry superseding R4-M3a's derivation clause | Q7, A-5 | `MirDifferenceTest` requirement column; ledger re-baseline | +60 t | D+R (Q8), R4's A6 lane | adv |
| H1a (new) | Split `Binding.Check`; eager publication at the end of a Final pass; DEC entry refining R2b-D2 | G2, Q8 | bp; Bind timing and allocation recorded (cold and warm); `HirColumnsTest`, `MirBuildTest` | ±70 | R | no |
| H1 | §2.7: `CheckTransfers` (S1, S2), S3 at closure binding, republication on failure, 10 old sites and the enum member deleted, ledger rows reclassified | H1a, R4's exit, G11, Q14, Q7, H0, OQ11 | as plan; Q14's cases as regressions (each fails before, passes after); `MirInitTest` | +100/−75 | final review of Q8; G:R6-check-flip | no |

Group G:L is Q9–Q12 (no merge before its review; Q11's jump part feeds A-8 and so G0). The asks change: A-5 becomes "each
recording rule names its requirement" (§2.6); A-8 is the one-line `Rejected` row of §2.3.

## 6. Alternatives considered

| Topic | Alternative | Why rejected |
| --- | --- | --- |
| Lint ownership (fix inside the owning component vs shared structure) | Keep the lints in `ControlFlowAnalysis` and only move it into Binding's folder (as Q6 moved `StructuralCompletion`) | CFA is a flow analysis of the old path deleted at H10; its lint half reads only Binding facts and syntax, so P5 is its owner (ARCH target pipeline). Moving the class would carry the flow code and its Koto-keyed tables into Binding |
| Lint records | Node failures (`BindingFailure.*`) | Binding incomplete makes the old analysis skip whole programs until H5 (PD31); and no new members (PD21) |
| Lint records | A new requirement per lint | Requirement vocabulary growth for checks that one condition number distinguishes |
| Lint condition numbering | `(ushort)DiagnosticCode` as condition | Couples the check identity to the code, which DIAGNOSTICS keeps independent (§4.1 "Check: requirement and condition number (independent of the code)") |
| Lint placement | Inline checks at each binding site | Discard and unsafe lints read final failure states and run in Provisional and tentative bindings; one Final walk is shorter and order-free |
| Lint placement | Fuse into the HIR publisher walk | The publisher decides nothing and runs only when MIR is built before G2 |
| FunctionFallthrough | In A3 (PLAN10) | Second completion rule; PD32 |
| FunctionFallthrough | Three call sites in the body binders | Tentative passes, three sites; one Final call reads the same `ResultStructure` |
| Jump targets | §2.3 rejections | — |
| Q17 | The plan's code → requirement table with fact disambiguation | §2.6; kept as the fallback |
| H1 | (b) restructure `Consume` | §2.7 table |
| H1 | Judge at the publisher's visit of each child (its row is final then, since writers precede it in pre-order) | Makes the publisher decide; relies on an ordering invariant that needs its own check; the post-publication scan needs neither |
| H1 | Non-failing records at lazy publication | `Binding.ReportDiagnostics` runs before MIR builds (`Project.cs:503, 506`); the builder would still build the Move and A1 would report a dependent |
| H1 S2 | Record discarded items in the column (the block takes them) | The builder reads an item's Adjust as "used as a value" (`MirBuilder.cs:274`, `Result` at `:325-331`): a builder change in R4's files and golden churn, for a rule H1 can judge with the same `BareAcquire`. Recorded as an R7 item (inline writes) |

## 7. Deletions, size and speed

| Area | Removed | Added | When |
| --- | --- | --- | --- |
| `ControlFlowAnalysis.cs` lint half (discard, unsafe, label, jump, fallthrough checks, repairs, `ReadsBinding`, `GuessedBy`, `SuppliedCompletion`, warning priority code) | ≈ −450 of 1,558 | — | Q9–Q12 |
| Binding lints, jump rows, end arrival | — | ≈ +480 | Q9–Q12 |
| Old TransferRequired sites, enum member | ≈ −75 | — | H1 |
| H1 scan, capture rule, `Check` split | — | ≈ +170 | H1a, H1 |
| `OwnershipFailure` → `OwnershipRequirement` | ±30 | — | Q7 |
| Q16, Q17 | ≈ −40 | ≈ +95 (t+60) | W1; Q17 |

Net Q stream from this package ≈ +150 until H10, after which the CFA remainder (≈ −1,100) and the transitional reads go.

Speed: Q9–Q12 add one Final walk to Bind and remove the lint checks from CFA's walk, which stays until H10 (Bind up,
Analyze down; both measured in Q9); after H10 Binding's walk replaces CFA's heavier flow walk. H1a costs one publication per
Final Bind only where MIR was not built anyway; H1's scan is linear over pooled arrays (0 B). Every new structure is pooled per
pass; no Koto-keyed collection is added.

## 8. Risks and questions

**Risks**

1. S2 and S3 (§2.7): the old-path side is read in code (the old analysis rejects both); the MIR side (the builder Copies a
   non-Copy discarded Place or an inferred capture, and `MirValidator` does not check that a `Copy` operand's Type is Copy) is
   inferred (H). If Q14 finds either path already closed, that check is dropped from H1; if it finds more paths where the
   builder decides an acquisition without the column, each becomes an H1 source or a column fix before H1.
2. Requirement change ControlFlow → Binding for 12 codes is visible in `kimi check` JSON (public vocabulary); record order at
   one location may change. Snapshot classification in Q9–Q12.
3. Eager publication changes when `HirTables.Broken` is computed; a node whose facts are still being completed by `Check`'s
   counting phase would be reported broken. H1a keeps every fact-producing step before publication (bp check: HIR snapshot
   equal).
4. Q17's departure needs R4's agreement while R4's A6 lane is active (A-5).

**Open questions for the design review**

1. Q17: the check names the requirement (D6) or the plan's table with fact selection?
2. H1: post-publication scan with republication (chosen) or judgment at the child's visit?
3. FunctionFallthrough on MIR: does A6 derive A1's return-delivery record from `FallthroughKey`, or does the builder build the
   end arrival of a rejected body as a derived Opaque value? (H0 decides; this package only publishes the key.)
4. Should CFA read Binding's target rows before H10 (one reader of the fact) or keep calling the pure rule (no freeze edit)?
5. V3 of §2.5: does an existing signature fact tell a constructor from a destructor?

**Owner decisions** (inside the OQ10 approval): D6 and its fallback; the R2b-D2 refinement (H1a); the new unit H1a and Q13's
closure; the public requirement-name change of the 12 lint codes.

## 9. DEC entry after OQ10 (draft)

- **R5-Q8 R6-check prerequisite package** (owner, after the design review; OQ10, R5-M1) The design
  `draft/Sketches/R5 Step 0/Q8-design-<n>.md` chosen by the review is the owner-approved design of DEC Q6 form 1 for Q7, Q16,
  Q17 and H1a, and of form 2 for G:L (Q9–Q12); H1 takes it with the final review in G:R6-check-flip. It decides: Ownership
  requirement kinds in Diagnostics; one HIR lint component (12 rows, PD31's form, condition = row); Binding-owned jump
  targets with the HIR contract on null targets; FunctionFallthrough in result validation (PD32, departing from PLAN10
  §5.5/§9); the Q16 views; MIR problems naming their requirement (superseding R4-M3a's derivation clause); TransferRequired as
  a scan of the published Adjust column with discarded Places and inferred captures, after an eager Final publication
  (refining R2b-D2); `Binding.ClosureInstances` and `Binding.RequirementInstances` deleted at R7. Each component, contract
  change and superseded entry is recorded again by the unit that first implements it. Rejected: restructuring `Consume`
  (option (b)); node failures for lints; a code → requirement table (fallback only if R4 declines A-5).
