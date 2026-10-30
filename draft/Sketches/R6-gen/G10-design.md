# R6-gen G10: the zero-reference rows

Status: G10's census and design (plan §4.9 row G10, §5.4 item 4; review G:R6-gen-del; Major deletion-side test rows under R5-M152 S7,
no design review), revision 0 on `claude/r6-g10` from `25570bb00` (G2-G9 landed, R5-M162-R5-M165). A working record, not authority.
Paths: `T/` = `tests/xUnitTest/Tests/`; row lines are `T/ArchitectureRulesTest.cs` at `25570bb00`.

## 1. Census (2026-10-31, `25570bb00`)

| Name | `src` (every folder, every file type) | `tests` (every folder) | `scripts` |
| --- | ---: | ---: | ---: |
| `BodyLowering`, `EmissionModule`, `LlvmModuleWriter`, `LlvmEmitter`, `GenericStoragePlan` (substring, comments included) | 0 | 0 | 0 |
| `\b(?:Failed\|Failure\|Analyze)Instance\b` (the old analysis's instance entry points, G7) | 0 | 0 | 0 |
| `LegacyRuntimeHelpers` (Backend, `[Transitional("R8")]`) | 139 lines | (included) | 0 |

- `Binding.FailedInstancePart` (Binding.TypeRelations.cs) is not `FailedInstance` (the trailing `\b`).
- History text naming the deleted components stays in `docs/` (DECISIONS, PLAN_HISTORY) and `draft/`: out of scope.
- `bin/`/`obj/` of a fresh worktree hold no such name (generated files come from current sources).

## 2. Design

- **One pattern, four rows** of `SourcePatternDoesNotGrow` at limit 0 (the existing table; no new mechanism): the five components
  R6-gen deleted (R5-M162, R5-M163) and the old analysis's instance entry points (R5-M162's instance half), over `src/Kimi`,
  `src/Benchmark`, `src/Playground` and `tests`. The plan's "src" is the C# sources: `src/kimi-ext` (TypeScript; its `node_modules`,
  ~17k files, costs ~0.4 s per walk in a checkout that has it), `src/backend` (its one `.cs` is `invoke-verification`'s job object)
  and `src/third_party` hold no compiler C#.
- **Comments count.** A comment naming a deleted component is stale text or history, which belongs in DECISIONS and commits; the count
  is 0 today, so a plain identifier pattern stays the one form. The row's source spells each name with a bracketed letter (`:50`'s
  convention), and its comment names no component.
- **Prefix match** (`\bLlvmEmitter`, no trailing `\b`): a component's stems and nested names (`BodyLowering.Calls`, `LlvmModuleWriterState`)
  count; the instance entry points keep `:61`'s trailing `\b`.
- `LegacyRuntimeHelpers` is excluded until R8 (plan §4.9).

## 3. The zeroed rows (R5-M162 Q3)

| Row | Limit | G10 |
| --- | ---: | --- |
| `:39` Emission failure strings | 0 | **Stays** (M8's Emission row): the surviving publication file has `Fail(compilation, code, cause)`, so the row still bars a failure string in `C/Emission` |
| `:55` entry points, Emission half (`.Emission.WriteIr\|Validate`, `.Emission.TryPrepare(out _,`) | 0 | **Deleted**: `Compilation.Emission` was an `LlvmEmitter` (`Compilation.cs:118` at `70b530151`), whose zero-reference row bars the type, so none of its members can be read. The Ownership half stays at 0 (the old analysis lives until R6-check); the name loses "and generation" |
| `:60` prepared emission module (`.Emission.TryPrepare(out x`) | 0 | **Deleted**, as `:55`'s half |
| `:62` generation failure facts (`.Emission.FailureIsResourceLimit\|ReportFailure`) | 0 | **Deleted**, as `:55`'s half |
| `:61` instance analysis (`(Failed\|Failure\|Analyze)Instance`) | 0 | **Folded** into the zero-reference pattern: tests only → `src` and `tests` (§5.4 item 5's "`AnalyzeInstance` deleted" becomes a row) |

The other rows (`:56-59`, R4/R6-check's Ownership reads and control-flow entries) are not zeroed and do not move.

## 4. M8's Emission rows (plan §5.1)

Prepared module 119 → 0 and generation failure facts 21 → 0 (both now the `LlvmEmitter` row), instance reads 11 → 0 (folded), Emission
failure strings 446 → 0 (kept). M8's fifth, `Ownership.Bodies` 30, is R4/R6-check's.

## 5. `EmitFixture`'s constants

Done by P0b (`bdc7fb48e`): `ScalarEmissionTest.EmitFixture` reads `AbortReasons.IntegerDivisionZero` and `.IntegerOverflow` (`T/ScalarEmissionTest.cs:78`).
Its `__kimi_entry_body`/`__kimi_f<N>` names are PD8's independent naming check, not P0b records (`ModuleCodegen.FunctionPrefix` is the
generator's own); nothing to re-point.

## 6. Records and expected diff

- DECISIONS: an amendment line on R5-M162 (Q3 done: rows replaced as §3); no new entry (the plan and Q3 decided the rows; no new
  representation, component, vocabulary or temporary). ARCH, CODEMAP, PLAN: none (PLAN is the planning session's).
- `T/ArchitectureRulesTest.cs`: +1 constant, +4 rows, +3 comment lines; −3 rows (`:60-62`), `:55` shortened: ≈ +8/−4. DECISIONS +4.
- Checks: incremental and non-incremental `-warnaserror` (tests project), `Kimigayo.slnx -warnaserror`, verify Unit `ArchitectureRulesTest`.
- As implemented: as designed; tests +15/−6 (the constant and the rows carry 5 comment lines), DECISIONS +5. A probe string counts 6
  (`LlvmEmitter`, `BodyLowering.Calls`, `AnalyzeInstance`, `GenericStoragePlans`, `LlvmModuleWriterState`, a `//` comment naming
  `GenericStoragePlan`) and 0 for `FailedInstancePart` and `IEmissionModule`; each row's folder exists (741, 40, 1 and 840 `.cs` files)
  and counts 0.
