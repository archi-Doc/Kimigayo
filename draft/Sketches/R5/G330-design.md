# G330: one rule for a thrown code generation contract failure (design)

PLAN G330 (G9b's review, 2026-10-31; R5-M167 "Open after the exit"). Design phase only: no source change is committed. Line numbers
are at `b63e00785` (dev). Paths: `C/` = `src/Kimi/Compiler/`, `P/` = `src/Kimi/SolutionAndProject/`. **(H)** marks an unchecked claim.
Authority read: PLAN G330; DECISIONS R5-M64, R5-M165 (and its design `draft/Sketches/R6-gen/G9-design.md`, rulings Q4-Q9, review 1
B1), R5-M167, BU-24 and BU-36 (the established broken-contract forms); DIAGNOSTICS rule 9, §4.2 and §6; SPEC §23.2.2; IMPL §20.6.4;
AGENTS "Implementation Design" and "Diagnostics".

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G330-1 | Code generation already has the two forms every other phase has: a **checked** broken contract with a subject is a recorded `InternalInvariant_Kd` (96 sites), and an **unchecked or unlocated** one throws (55 sites, plus framework indexers and `!`). The front end ends a thrown one as `CheckFaulted_Kd` (kind `Exception`, DIAGNOSTICS rule 9 and §6); only generation maps it differently, by an incomplete type list | 1 |
| G330-2 | The rule: during generation an `IOException` or `UnauthorizedAccessException` is the output's (`ArtifactPublicationFailed_Kd`); every other exception is code generation's broken contract and ends the Emission result as Faulted through the one fault path (`CheckFaulted_Kd`, `Exception`, its message the bounded note), at the command's result boundary, as the front end's does. Nothing escapes a command | 2 |
| G330-3 | Recommended: option C (the shared fault path). It deletes `GenerateInto`'s type-mapped contract catch and the R5-M165 clause that maps "argument, data and support exceptions" to `InternalInvariant_Kd`; it supersedes that clause (DECISIONS) | 3 |
| G330-4 | Major, one unit of ≈ 50 min; no vocabulary entry, no new code, no owner question | 6 |

## 1. Census

### 1.1 Throw sites reached by generation (`C/Backend/**`)

55 throw sites: `InvalidOperationException` 50 (45 direct, 5 through `Layout.Outside`), `InvalidDataException` 3, `ArgumentException` 1,
`ArgumentOutOfRangeException` 1; none throws `IOException`, `UnauthorizedAccessException` or `NotSupportedException`.

| Area | Sites | Contract checked |
| --- | ---: | --- |
| `Backend/Codegen/**` | 21 | 14 switch defaults over a closed MIR or Backend vocabulary ("has no code generation row": statement, rvalue, terminator, operand, callee, cast, aggregate, projection, body, parameter, mono item kinds); `FunctionCodegen.Arithmetic.cs:150` table completeness at type initialization; two check writers (`:267`, `Conversions.cs:198`), a home's spelling (`Spelling.cs:126`), a storage kernel (`Storage.cs:59`), a drop verdict (`Drops.cs:98`), a kind-bearing formatting row (`ModuleCodegen.Formatting.cs:46`) |
| `Backend/*.cs` | 24 | Abi: a C signature's flags and parameter (`Abi.cs:198`, `:218`), a Type Layout does not lay out (`:227`); `Layout.Outside` ×5 ("Layout has no row for the Type"); `IntrinsicRows.cs:118`, `:129` (row lookup, table completeness); `BuiltinArithmetic.cs:29`, `Capability.cs:74`, `LayoutNeeds.cs:50`; `LegacyRuntimeHelpers*` ×6 (row lookups, an Option payload offset); `LlvmConstantPool.cs:41` (`ArgumentOutOfRangeException`, index), `:58` (`ArgumentException`, an empty string has no constant); `LlvmModuleText.cs:195`, `:203` and `AbortReasons.cs:75` (`InvalidDataException`: the compiler's own templates and abort texts) |
| `Backend/Instances/**` | 10 | Closed-vocabulary rows of the collector (storage verdict, mono item kind, intrinsic target, layout outcome, callee kind, witness kind and slot, a property witness that is not a Field, a shim target). The collector runs at check (`Compilation.Pipeline.cs` `Collect`); generation calls `Generates`, `Subject` and `Mark` |

Framework operations that throw on a broken contract inside `Backend/Codegen/**`: 118 direct index expressions over arrays and lists
(`IndexOutOfRangeException`, `ArgumentOutOfRangeException`), 72 null-forgiving `!` (`NullReferenceException`), dictionary indexers only on
static tables (`Layout.Tag`, `BoundType.Primitives["u8"]`: `KeyNotFoundException` only for a broken table), and a table check in a static
initializer surfaces as `TypeInitializationException`. Binding queries that generation calls (`TyCtxt`) throw 36
`InvalidOperationException`, 5 `ArgumentOutOfRangeException` and 2 `UnreachableException` under the same contract form.

The checked form: `FunctionCodegen.Report` and `ModuleCodegen.Report` record `InternalInvariant_Kd` at a subject (81 sites in `Codegen`,
15 in `Instances`), and `Compilation.Generate` at the project for an unexplained refusal (`Compilation.Generation.cs:70`, `:89`, `:119`,
`:134`, `:152`). `ModuleCodegen`'s contract forbids catch-alls in code generation.

### 1.2 The escaping path today

`EmissionArtifacts.GenerateInto` (`C/Emission/EmissionArtifacts.cs:104-120`) catches `IOException`/`UnauthorizedAccessException` as the
output's and `ArgumentException`/`InvalidDataException`/`NotSupportedException` as `InternalInvariant_Kd`. Anything else leaves `Publish`
(whose own catch, `:85`, maps publication's operations) and `Project.PublishArtifacts` (`P/Project.cs:263`) unrecorded.

Probe (throwaway, reverted; an environment-gated throw at the start of `ModuleCodegen.Write`, `Kimi.exe` built to a scratch folder, a
Hello Application with `kimi emit` and `kimi build`):

| Thrown | `kimi emit` / `kimi build` today |
| --- | --- |
| `InvalidOperationException`, `KeyNotFoundException` | a plain line with the message, no code, no location, exit 1: `Project.Build`'s and `CommandExecution`'s `NativeToolchain.IsToolchainFailure` catch (`P/Project.cs:229`, `NativeToolchain.cs:25`), which lists both types, reports it as a toolchain failure |
| `IndexOutOfRangeException`, `NullReferenceException` | .NET "Unhandled exception" with the stack trace; the process dies (exit 127 under Git Bash) |
| `ArgumentException` | `InternalInvariant_Kd` at `Hello.kimiproj`, no note, exit 1 (R5-M165) |

`kimi test` (code read): `TestCommand.cs:220` catches it as "Internal test runner error: …", exit 3, although IMPL §20.6.4 makes a build
failure exit 2. The language server never generates (`CheckService.Run` → `Project.Check`, `emit` false); an exception in its front end is
already `CheckFaulted_Kd`. Tests and `src/Benchmark` call `Compilation.Generate` directly and see the exception with its stack.

### 1.3 How other phases end a thrown contract failure

One fault path (DIAGNOSTICS §6, rule 9: "a Faulted result is explained by exactly one `CheckFaulted_Kd`"; "`InternalInvariant_Kd` is
reserved for internal failures in a Completed result"): `CheckService.Run` (`src/Kimi/Checking/CheckService.cs:51`) and the command's
`Project.BuildTarget` (`P/Project.cs:444`) turn any exception but cancellation into `DiagnosticFaults.Create(Exception, message, project,
kept records)`, the command through `Project.Publish(..., fault)` (`P/Project.cs:620`). BU-24 and BU-36 record the same split for
Binding: a located broken contract is `InternalInvariant_Kd`; one without a node throws and ends the check as `CheckFaulted_Kd`. MIR
construction and `MirValidator` record located findings the same way.

## 2. The rule

A code generation contract failure that code generation **checks at a subject** is a recorded `InternalInvariant_Kd` at that subject (or
at the project for an unexplained refusal), as today. One that **throws** (a closed table without a row, a missing fact, a framework
operation's contract) is not mapped by type: while code generation writes the output, an `IOException` or `UnauthorizedAccessException`
can only be the output writer's (code generation does no other I/O, a census fact `ModuleCodegen`'s contract will state), so it stays
`ArtifactPublicationFailed_Kd` with its cause; **every other exception** propagates out of the generation entry and of `EmissionArtifacts`
unclassified, and the command's result boundary ends the Emission result as Faulted with the one fault record, `CheckFaulted_Kd` (kind
`Exception`, the message as the bounded note, at the project file), exactly as it ends a front-end result. Nothing escapes a command; this
is not a hidden failure, because the command reports one Internal Error and fails (`kimi build`/`emit` exit 1, `kimi test` exit 2, IMPL
§20.6.4), and direct callers (tests, `src/Benchmark`) keep the exception and its stack. Owner: the one fault path (`DiagnosticFaults` and
the command's result boundary in `Project`), which already owns this decision for every other phase; the generation entry's contract
states that it throws for an unlocated broken contract, and `EmissionArtifacts` owns only the output's failure. Deleted: `GenerateInto`'s
contract catch (`ArgumentException or InvalidDataException or NotSupportedException` → `InternalInvariant_Kd`), R5-M165's clause that maps
"code generation's argument, data and support exceptions" to `InternalInvariant_Kd` (superseded), and the same clause in DIAGNOSTICS
§4.2's Emission row. Kept: the missing embedded backend record (`Publish`'s check before the `try`, R5-M165 Q6), a checked condition.

## 3. Options

| | A: one contract exception type, mapped once | B: the generation entry records every non-I/O exception | C: the shared fault path (recommended) |
| --- | --- | --- | --- |
| Shape | Every code generation check throws one type (a new `CodegenContractException`, or `UnreachableException`); `GenerateInto` maps it to `InternalInvariant_Kd` | `GenerateInto` (not `Generate`, see below) maps every exception but the output's I/O and cancellation to `InternalInvariant_Kd` at the project | §2: the exception propagates; `Project.PublishArtifacts` catches it under the same filter as `BuildTarget` and renders the Emission result's fault |
| Owner | Backend (every check) + `EmissionArtifacts` | `EmissionArtifacts` | the one fault path; `Project` executes it for both results |
| Deletes | the type list | the type list (one filter becomes "every other") | the contract catch and the R5-M165 clause |
| Size | ≈ 55 throw sites retyped (34 in shared Backend files that check also reaches: Layout, the collector), +1 type | product ≈ −4 | product ≈ +7 (§4) |
| Covers framework exceptions | **no**: 118 indexers, 72 `!`, `TypeInitializationException`, `TyCtxt`'s throws still escape; G330's `KeyNotFoundException` and `IndexOutOfRangeException` stay | yes | yes |
| One form across phases | no: a second form | no: a thrown failure is `InternalInvariant_Kd` without its message in generation and `CheckFaulted_Kd` with it elsewhere, against rule 9 | yes (rule 9) |
| Risks | churn in shared Backend files (Layout, Abi, IntrinsicRows are also check-time); a new concept for one phase | loses the message (R5-M165 records no note), so a user report says only "the project"; reads like the AGENTS catch-all | the fault text says "while checking … ended the analysis" for generation (Q2); a local in `Publish` (§4); `kimi test`'s exit moves from 3 to 2 (a correction, IMPL §20.6.4) |

Placements rejected for every option: a catch in `Compilation.Generate` (tests and `src/Benchmark` would get a record instead of the
exception and its stack, and `ModuleCodegen`'s no-catch-all rule would move one call up); a catch in `CommandExecution` (it would also take
the front end's and the toolchain's exceptions, and it has no result to render into). A new `DiagnosticFault` kind (`Generation`) for the
text is not needed: rule 9 makes the fault kind the Reason, and the Emission result is rendered as its own result after the front end's.

**Recommendation: C.** It is the only option that is both complete (every exception is either the output's or a fault) and consistent
(the decision "an exception ended a phase's result" has one owner and one form, AGENTS "Share by meaning"); it removes a type allowlist
instead of extending it, and it keeps the exception's message, which `InternalInvariant_Kd` at the project lacks.

## 4. Implementation sketch (C)

- `EmissionArtifacts.GenerateInto`: keep only the output's catch (`IOException or UnauthorizedAccessException` →
  `ArtifactPublicationFailed_Kd` with the message); its summary states that every other exception is code generation's and propagates.
- `EmissionArtifacts.Publish`: a local `generating`, set around the `GenerateInto` call inside the writer's `using`, as `BuildTarget`'s
  `completed`; the catch becomes `when (!generating && ex is IOException or InvalidDataException or …)`, so publication never classifies an
  exception code generation threw (its own types overlap code generation's `ArgumentException` and `InvalidDataException`). Alternative if
  review rejects the local: three regions (inputs, generation, commit) with one `IsPublicationFailure` predicate, ≈ +8 lines.
- `Project.PublishArtifacts`: `try { if (EmissionArtifacts.Publish(...)) return true; } catch (Exception ex) when (this.Faults(ex, context))
  { fault = ex; }` then `this.Publish(..., Emission, Emission, context, fault: fault)`. `Faults(ex, context)` (`context is null &&
  RendersDiagnostics && ex is not OperationCanceledException`) is the one filter, which `BuildTarget`'s catch then uses too.
- Contracts: `Compilation.Generation.cs` (a thrown broken contract ends generation; the caller's fault path reports it, DIAGNOSTICS rule 9),
  `ModuleCodegen.cs` (no I/O but through the output writer), `EmissionArtifacts` (the output's failure only).
- Unchanged: `NativeToolchain.IsToolchainFailure` (generation no longer reaches it; its own list is R9's toolchain, see Q4).

## 5. Tests (fail first, lowest public layer)

- T1, `EmissionArtifactsTest.GenerationIntoTheOutputSeparatesOutputFromContractFailures` (the existing `FailingWriter` seam): rows
  `IOException`, `UnauthorizedAccessException` → one `ArtifactPublicationFailed_Kd`; rows `InvalidOperationException`,
  `KeyNotFoundException`, `IndexOutOfRangeException`, `ArgumentException`, `InvalidDataException` → `GenerateInto` throws that same instance
  and the Emission partition is empty. Fails before for the `ArgumentException` and `InvalidDataException` rows (recorded today).
- T2, the command layer: `Project.PublishArtifacts` on an analyzed compilation whose code generation throws renders one `CheckFaulted_Kd`
  (Reason `Exception`, the message as its note, at the project file), returns false and leaves no `.ll`. Injection without a production
  seam: give the compilation another compilation's `ModuleCodegen` (its private `moduleCodegen` field; tests already use non-public
  reflection, 10 sites), a broken "one `ModuleCodegen` per compilation" contract **(H: that it throws rather than records; the unit's
  reproducer step confirms it first, else uses a `FailingWriter`-backed internal path and records the gap)**. Fails before: the exception
  leaves `PublishArtifacts`.
- Kept: the four `DoesNotContain("InternalInvariant_Kd")` of R5-M165 Q7 (they assert no checked contract record); `kimi test`'s exit for a
  generation fault (2) is asserted where `TestCommand`'s build-failure exit is tested **(H: whether such a test exists)**.

## 6. Classification and sizing

Major (WORKFLOW): not Minor (a catch and a filter in a second component, a responsibility moved from `EmissionArtifacts` to the fault path);
not Full (no shared representation changes: `CheckFaulted_Kd`, `DiagnosticFault.Exception` and `Project.Publish(..., fault)` exist). The
design review is this design's ruling on Q1. One unit, G330a, ≈ 50 min: product (§4), T1 and T2, DIAGNOSTICS §4.2's Emission row,
DECISIONS (a new entry superseding R5-M165's mapping clause, with amendment lines on R5-M165 and R5-M64), CODEMAP only if navigation
changes (it should not); unit checks `EmissionArtifactsTest`, `CheckServiceTest`, `DiagnosticContractTest`, `LibraryEmissionTest`,
`PipelineSwitchTest`, the `kimi test` command tests, then a Session. If T2's injection fails, split: G330a (rule, T1, documents) and G330b
(T2). No vocabulary entry (no code, fault kind or closed-vocabulary row is added), so no owner approval.

## 7. Open questions

| # | Question | Recommendation | Who |
| --- | --- | --- | --- |
| Q1 | Adopt C (the fault path, superseding R5-M165's mapping clause; the PLAN row's "belongs to the generation entry" read as: the generation entry's contract states the rule, the one fault path executes it), or B (keep `InternalInvariant_Kd`, widen the filter)? | C | planning session |
| Q2 | The fixed fault text ("The compiler failed internally while checking: an exception ended the analysis") for a generation fault | keep now; any rewording of `CheckFaulted_Kd`'s text is a Diagnostics wording unit for every phase, not G330's | planning session |
| Q3 | T2's reflection injection, or T1 plus the CLI probe recipe as evidence only | T2 if the reproducer throws | planning session |
| Q4 | `NativeToolchain.IsToolchainFailure` lists `InvalidOperationException` and `KeyNotFoundException`, so any compiler exception under `Project.Build` outside generation (for example `RecordLibrary`'s `BuildMetadata.Single()`) prints as a toolchain failure | an open PLAN issue for the toolchain boundary (R9), outside G330 | planning session |
