# H7 design: delete Binding's 128-bit capability gates (R6-check, G:check-del)

Base: dev `50c6e1f50`. Read through git only; no build, test or CLI run (W-18 running). (H) marks an unverified inference.
Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. Working time: 2026-11-05 13:30-13:47 (17 min of Q12's hour; design-state.md).

Unit (H0-design §4 row H7, H0c row H7, owner-approved through R5-M182; moved to R5 by C1, PLAN `50c6e1f50`): delete Binding's
wide-division gate; A3b's `Capability.WideDivision` owns the rule. Prerequisites: the sweep (N1), K9a, G157 (1) (answer BU-4).

## 1. Decisions

| # | Decision | Reason |
| --- | --- | --- |
| D1 | **Listed prerequisites: all met.** Sweep: closed 2026-11-05 (PLAN `4fb6fcdb1`, C-13). K9a: landed 2026-10-18 (`ef22aad88`, merged by `154e10a81`, PLAN `54999b781`; DECISIONS R5-M53; `C/Backend/Capability.cs`, `C/Backend/Instances/InstanceCollector.Capabilities.cs`). G157 (1): done (R5-M183; U1 batch 95 merge `6147cce56`, U2 batch 96 merge `f7ea5e27d`; PLAN `3283d9aaa` "H7's prerequisite met"). Dev PLAN G157 (1) now holds only U1's review N1 (`CheckStatic`), which is not H7's | git ancestry and PLAN rows |
| D2 | **Scope: both closed-form gates**, not the division gate alone. (a) `C/Binding/Binding.Operators.cs:331-336` (the gate is `:333-335`; H0c's `:331-334` is one comment line off): the `left.IsNumeric` branch returns `Complete(binary, result)`, merged with the PrimitiveInteger-proven branch `:326-329` (same result), about -6 lines. (b) `C/Binding/Binding.Conversions.cs:573-580`: an integer/float conversion is always `ConversionBinding.Numeric` (the `<= 64` test and its `Fail(Unsupported)` go), about -6 lines | ARCH's Capability row and R5-M53 already say Binding's gates for closed `WideDivision` **and** `WideFloatConversion` forms go at H7; keeping (b) leaves a second copy of the rule (AGENTS symptom list) and ARCH false. H0c's row names only (a): an omission. With both, a search finds no other site with the cause (WORKFLOW Minor) |
| D3 | **What A3b publishes instead:** `Unsupported_Kd` (Unsupported, SPEC 23.1.1; IMPL 21.3.3 "128-bit subset") from `InstanceCollector.CheckOperation(body, statement)` (`InstanceCollector.Templates.cs:122`), at `Located(body, statement.Source)`: the operation's own node (`MirBuilder.Operators.cs:76-80` sets the statement at the `BinaryKoto`; a compound form builds the same operation, R5-M53), once per location, in every block of every root-module MIR body (unused, unreachable, check-only, default templates, StaticInit, closures, closed operations in generic bodies). Same code, same location as Binding's `Fail(binary, Unsupported, unresolved: true)` today; published by Core's publisher under ControlFlow instead of Binding | `Capability.cs:83-99`; R5-M53; `Compilation.Publication.cs` header |
| D4 | **Acceptance and independent records (SPEC 23.1.2, Q13): not shown clean; two unlisted dependencies found (H).** (i) **Acceptance gain (regression 1 unless named):** a Field initializer of a struct with no constructor (its omitted base clause selects none, SPEC 6.3.6) lies in no MIR body (G215; `MirBuilder.Constructors.cs:13-26` builds initializers only into constructors; body kinds `MirVocabulary.cs:197-206`), so A3b never sees `public var n: i128 = 7@i128 / 2` there; A5's G215 (U) record covers only Origin rows and Fields without an initializer. Removal: R7-O3 (a MIR body of the layer's initializers). (ii) **Lost independent records (regression 3 unless rule W):** HIR replaces every operand of a parent Binding rejects (calls, rejected operators and conversions, failed statements, an un-annotated failed `let`, failed match and constructs; `Binding.Hir.Operations.cs:161-166, 233-247`; PLAN G121 "Lost independent errors"), so a 128-bit operation under such a parent (`x / 3 + "s"`, `f(x / 3)` with `f(a: i64)`) is never built and A3b reports nothing, while today Binding's gate reports it (the parent's own check reads the failed operand and stays silent). (iii) Gains (dependent, counted by code, not blocking): an independent error the failed operand hid today, e.g. `let y: i64 = x / 3` adds TypeMismatch_Kd at the `let` beside A3b's record (the mismatch is recorded on the variable, `Binding.Variables.cs:97`, so the operation is still built); `(x / 3)@f64` reports both operations | code reading; G215 and G121 rows state the mechanism; the probes in §3 decide |
| D5 | **Consequence:** H7 cannot land clean under Q13 until (ii) is settled and (i) is named or removed. Options for R5: **(A)** hold H7 behind G121's operand residuals (R6-check, R5) **and** its constructs (owner, unscheduled) **and** R7-O3; that is past R6-check's exit, so BU-4's clause applies (retag with the owner's approval). **(B)** one owner question: name (i) as an acceptance move until R7-O3 (the initializer never runs; no miscompile; STATUS limit) and accept (ii) as G121's class (record loss only on programs already rejected), then H7 lands now. **(C)** R5 rules (ii) is rule W (the replaced operand is the parent's Opaque, an input resting on the parent's reported record); this contradicts G121's own wording, so a reviewer may block. **Recommend (B), asked non-blocking after the probes confirm**: H7 is -12 lines of a duplicate rule; (A) parks it for an unscheduled construct design | Q13 (owner asked acceptance only; record selection is the planning session's, but a lost independent record is a listed regression) |
| D6 | **No other path loses or accepts (H):** no constant folding of binary operations (only direct-literal conversions fold, `Binding.Hir.cs:185-200`; `BinaryKoto.FoldedConstant`); a named function with failed parameter Types still completes and its body is built (`Binding.Functions.cs:89-102`); a body with a validator finding drops its A3b record as R5-M183 decides (dependent); library bodies are outside A3b (P5: the library binds clean today, so none has such an operation) | code reading |
| D7 | **Class: Minor** if the probes show no regression (one owning component, a deletion, no new branch/state, search clean per D2). If D5 (B) is taken: **Major** (the design names an acceptance move), no design review (the issue is the owner's, settled by the answer; WORKFLOW Design 4) | WORKFLOW "Change classes" |
| D8 | **Approval and review:** BU approves this design and consents to the two Binding hunks (C1). One review (Q13) by R4 or an independent reviewer R5 dispatches, not BU's review lane; R4 fits (A3b's records are MIR collection records it judges) | C1, Q13 |
| D9 | **Docs (AGENTS Auto):** ARCH row Capability (`COMPILER_ARCHITECTURE.md:149`): drop "which Binding's gates repeat for closed forms until H7 deletes them". DECISIONS: amend R5-M53 (line 10196, "Binding still refuses ... until H7": now A3b alone; the Opaque sentence goes) and add the C1 entry (owner 2026-11-05 「C1C3を採用してください」: R5 designs and implements H7 and H11, BU approves and consents) in H7's commit. Code comments: `InstanceCollector.Capabilities.cs:30-31` ("Binding's gate and this check never both report"), `T/InstanceCollectorTest.cs:486-489, 1965-1967, 1986`. STATUS row Scalars (`STATUS.md:74`): unchanged (still prohibited); with (B), add the constructorless-layer limit. DIAGNOSTICS, CODEMAP: no fact changes. PLAN: H7's new dependencies (Auto, blockers) | AGENTS update table |
| D10 | **Where it lands: alone, in the earliest window after D5 is settled**, not on `claude/r5-check-del`. It shares no file or dependency with H10b; grouping would hold G:check-del on D5's answer or H7 on the group. Before H10a lands, the window's mir ledger may move rows for programs with closed 128-bit operations (Binding now complete; the collector rejects) | H0c D-c3 ("any, after H5's sweep") |

## 2. Tests (the lowest public layer: the collector's records, `MirBuildTest.Compile` + `InstanceCollector.Run` + `Records(Collection)`; acceptance through `MinimalEmissionTest.Problems` and `Generate`)

| Test | Today (asserts Binding's record) | After H7 (the MIR's record; never weaker) |
| --- | --- | --- |
| `T/InstanceCollectorTest.BindingsRefusalsAreNotRepeated` (`:1975`, 6 rows) | Binding issue `Unsupported_Kd` at `x / 3`, `x /= 3`, `x %= 3`, `x@f64`, `x@i128`; no collection record; literal row: none | Merged with `AClosedOperationOfTheTableIsAGapAtItsLocation` (`:1996`) into one real-source theory: `Binding.Result.IsComplete`, no Binding issue, exactly one collection `Unsupported_Kd` at the same node text; literal row still none; the synthesized-MIR rows become sources (unreachable: after `return`); twins (i64 `/`, i128 `+`, `>>`, i64@f64) none. Fail-before: today the Binding assertion fails |
| `T/WideIntegerEmissionTest.ProfileExclusionsAreDiagnosedBeforeOptimization` (`:105`, 40 rows: `/ % /= %=` x i128/u128 x top/`if false`/unused, and 16 float rows) | Binding incomplete, a Binding `Unsupported` issue, `Generate` false, empty IR | Binding complete; `MinimalEmissionTest.Problems(c)` equals exactly `Unsupported_Kd <operation>` (e.g. `x / 1`, `x@f32`); `Generate` false, empty IR (stronger: the exact record) |
| `T/ConversionEmissionTest.InvalidAndUnimplementedAdaptationsHaveDistinctFailures` (`:85, 86, 88`) | Binding incomplete, issue `Unsupported`, `InvalidCount` 0, no IR | The three rows leave this Binding-failure theory for the collector theory above (record at `x@f64`/`x@i128`, no IR); the other rows unchanged |
| `T/FloatConversionEmissionTest.RemainingConversionsAreNotMistakenForWidening` (`:59-62`) | Binding incomplete, issue `Unsupported`, no IR | Binding complete; `Problems(c)` = `Unsupported_Kd x@f32` etc.; `Generate` false |
| `T/ElementUpdateEmissionTest.InvalidUpdatesProduceNoIr` (`:288-289`, `a[0] /= 2`, `%=`) | no IR (Binding) | unchanged assertion (no IR, now through A3b) (H); add the element-update row to the collector theory |
| `T/PositionWarningTest.ResolvableOrNonLiteralKeysAreNotWarned` (`:97-98`) | no `PositionAlwaysFails_Kd` (operand failed) | unchanged; like its i8 twin `:96` (H) |
| Guards, unchanged: `InstanceCollectorTest` K9b and shim theories (`:2037`, `:2075`), `ABodyWithAFindingDropsOnlyWhatRestsOnIt` (comment only), `PrimitiveIntegerTest.WideDivisionIsDiagnosedForTheInstance`, `GenericGenerationDiagnosticTest`, `BuiltinArithmeticContractTest` (`:103-115`), `NumericConversionEmissionTest` (literal conversions) | open and shim forms are K9b's | same |
| New (same rule, in the collector theory): default template `func f(a: i128, d: i128 = 10@i128 / 3)`, closure body, a closed operation in a generic body, `Wrapping<u128>` `/`, a scalar `let` static's StaticInit (not a `var` one: that adds `CheckStatic`'s storage gap) | — | one record each at the operation |
| New (SPEC 23.1.2, the gained independent record): `let x: i128 = 10\nlet y: i64 = x / 3` | today only `Unsupported_Kd x / 3` | `TypeMismatch_Kd` at the let and `Unsupported_Kd x / 3` (H); counted under Q13 |

## 3. Reproducer step 0 (the implementer, before code: a private CLI copy, dev vs the patch, exact sources)

P1 `let x: i128 = 10` + `let y = x / 3 + "s"` (rejected operator); P2 `missing(x / 3)` (failed call); P3 `func f(a: i64) => ()` +
`f(x / 3)` (no applicable candidate); P4 the annotated `let` above; P5 `let y = (x / 3)@f64`; P6 `open struct Base` with two
ambiguous `protected init` (as `OriginRowsTest.AConstructorlessLayersRowsLieInNoBodysSlice`) + `struct Leaf: Base` /
`public var n: i128 = 7@i128 / 2`; P7 `let y = if true => x / 3 else => "s"` (failed construct); P8 `let y = x / 3` +
`let z: i64 = y`. Rule: a lost `Unsupported_Kd` at a 128-bit operation that carries no failing record of its own is regression 3;
an accepted P6 is regression 1 unless named. Expected (H): P1, P2, P3, P7 lose; P6 accepted; P4, P5, P8 gain only.

## 4. Verification

Unit: the classes of §2 plus `ArithmeticContractTest`, `NonIntegerOperandDiagnosticTest`, `UserArithmeticOperatorTest`,
`ConversionReadTest`, `BitConversionTest`, `WrapConversionTest`, `WrappingIntegerTest`, `IntegerEmissionTest`; no
`KimiLibraryShapeTest` (library untouched); `-DiagnosticSnapshot` against the base: expected no difference (the corpus, milestones,
`docs/examples`, `tests/diagnostics/mutations.json` and `syntax.json`, has no closed 128-bit division or typed 128-bit float
conversion; `1e38@u128` is a folded literal) (H); no native fixtures (no generated code changes). Session at the session's end
(AGENTS). Commit counts Q13's record moves by code and cites Q13 for each moved expectation.

## 5. Open questions for R5

1. **(blocking H7's landing)** D5: (A), (B) or (C); my recommendation (B) after step 0 confirms D4.
2. (non-blocking) D2: confirm both gates in H7 (H0c's row named one); size about -12 code lines.
3. (non-blocking) D8: PLAN's C1 text says BU also reviews its Binding hunks; with Q13's one review, read BU's part as consent.
4. (non-blocking) Draft rows H0c/H0-design keep `:331-334` and "BU"; record the outcome there only if R5 wants the drafts current.

## 6. R5's rulings (2026-11-05)

| # | Ruling | Reason |
| --- | --- | --- |
| Q1 (D5) | Not decided yet. Step 0's probes (P1-P8) run first, after W-18 closes, in R5's own worktree; R5 then asks the owner with the evidence: **(A)** keep both gates past R6-check until G121's operand residuals and R7-O3 let A3b see every operation (a postponed removal: the owner's approval), or **(B)** land H7 now with (i) named as an acceptance move until R7-O3 and (ii) accepted as G121's class. (C) is rejected: (ii)'s records are not dependent (A3b reads no failed input; HIR never built the operation), so calling them rule W would contradict G121 and AGENTS' Diagnostics rule. | Q13: an acceptance move needs the owner's name; a lost independent record is a regression the planning session cannot rule away. |
| Q2 | Yes: both gates (division and float conversion) in H7, about -12 lines. | ARCH's Capability row and R5-M53 name both for H7; one copy of the rule. |
| Q3 | H7's one review is BU's independent reviewer, who also approves the design (C1; BU's answer of 2026-11-05: "I dispatch an independent BU reviewer and give the approval; the same reviewer then reviews the Binding hunks"). Not R4. | The owner's C1 gives the Binding units' review to BU. |
| Q4 | No draft edits; the outcome is recorded in DECISIONS (R5-M53's amendment, the C1 entry) and PLAN. | H0/H0c are design records. |
| D10 | Accepted: H7 lands alone in the earliest window after Q1 is settled. | No dependency on G:check-del. |

## 7. Step 0's result and R5's recommendation (2026-11-05; `probes.md`, dev `4862b9fc7`, patch on local `r5-h7-probe`@`b2dc74c12`)

- (i) confirmed: P6 and P6f (a constructorless layer's Field initializer with a 128-bit division or float conversion) are accepted.
- (ii) confirmed and wider: P1, P1f, P2, P2f, P3, P7 **and P4** (`let y: i64 = x / 3`: the failed `let` replaces its initializer, so
  only `TypeMismatch_Kd` remains) lose the operation's `Unsupported_Kd`; in P1/P1f the parent's `NonNumericOperand_Kd` becomes a
  `TypeMismatch_Kd` at `"s"`. P5, P8 gain only; P3 gains a dependent `NoApplicableOverload_Kd`. §2's new P4 test row is refuted.
- **R5 recommends (A)** to the owner: keep both gates until A3b sees every operation (G121's operand residuals under a rejected parent,
  R7-O3's layer-initializer body), i.e. H7 moves past R6-check to R7 (BU-4's clause; a postponed removal needs the owner's approval).
  Reason: (B) would accept an unsupported form silently (AGENTS Diagnostics) and lose independent records on ordinary programs such as
  P4 (SPEC 23.1.2, Q13), to remove a 12-line duplicate. Under (A) the gates are a temporary structure with a removal condition, recorded
  in DECISIONS and the ARCH ledger at H15.
