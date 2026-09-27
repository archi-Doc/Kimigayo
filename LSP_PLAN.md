# LSP Compiler Diagnostics Implementation Plan

Status: **PLANNED; implementation has not started.** Prepared on 2026-09-27 from source inspection at `81dab2f2`. This document records proposed work, not implemented support. The active compiler milestones remain in [PLAN.md](PLAN.md).

## 1. Objective and scope

Connect the existing LSP server to the compiler's diagnostics, including unsaved edits, while extracting a small analysis foundation that a future Compiler Server Protocol (CSP) adapter can reuse.

The completed LSP work includes initialization and shutdown, document synchronization, standalone and project analysis, syntax/Binding/startup/control-flow/ownership diagnostics, trailing-edge input debounce, stale-result suppression, and editor verification. It follows the compiler's implemented support boundaries and the selected analysis configuration.

This plan does not implement CSP, AST inspection or manipulation, automatic fixes, verification jobs, completion, navigation, formatting, pull diagnostics, incremental semantic compilation, or native execution from the editor. CSP remains a future consumer of the common foundation. Language requirements remain in [SPEC.md](SPEC.md) and [IMPLEMENTATION.md](IMPLEMENTATION.md); project semantics remain in [Chapter 18](spec/18-modules-and-dependencies.md).

## 2. Existing code to reuse

These are source-inspection findings; no compiler or LSP execution was performed for this plan.

| Existing component | Reuse and focused changes |
| --- | --- |
| [LspServer](Kimi/Lsp/LspServer.cs), [LspHelper](Kimi/Lsp/LspHelper.cs), [LspCommand](Kimi/Unit/Command/LspCommand.cs) | Keep the stdio entry point, framing helpers, handlers and initialized output lock. Inject streams and lifetime control for tests; separate receiving messages from analysis. Open/change diagnostic publication is currently disconnected. |
| [TextDocument](Kimi/Lsp/TextDocument.cs), [TextLine](Kimi/Lsp/TextLine.cs) | Keep ordered incremental edits and pooled storage where practical. Preserve exact line endings: current `ToString()` joins lines with LF. Add document lifetime and revision tracking. |
| [SourceDocument](Kimi/Compiler/Core/SourceDocument.cs) | Reuse source spans and UTF-16 position conversion, including CRLF/LF/CR handling. Avoid a second compiler-side location implementation. |
| [Diagnostic](Kimi/Diagnostics/Diagnostic.cs), [DiagnosticCollection](Kimi/Diagnostics/DiagnosticCollection.cs), [DiagnosticEntry](Kimi/Diagnostics/DiagnosticEntry.cs) | Retain codes, severities, messages, hints and source association. Preserve current deduplication, `HasErrors` and `ErrorVersion` behavior. Create immutable output records after collection. |
| [Kimigayo](Kimi/Unit/Kimigayo.cs) | Separate `ReportDiagnostic` console rendering from collection. Scope diagnostic state to an analysis attempt so retained named collections cannot leak earlier results. |
| [Project](Kimi/SolutionAndProject/Project.cs), especially `BuildTarget` | Extract the existing front-end sequence and acceptance checks. Share it with CLI check/build/test paths; do not create an LSP-specific semantic pipeline. |
| [DependencyResolver](Kimi/SolutionAndProject/DependencyResolver.cs) | Extend the existing fixed-input graph preparation with source overrides. `Project.AddSource` appends sources and is not an overlay replacement API. |
| [JSON DTOs](Kimi/Lsp/Json.cs), [LspJsonContext](Kimi/Lsp/LspJsonContext.cs) | Extend the existing source-generated serializer and diagnostic payload. Fix success responses requiring `result: null`, which the current null-omission setting would omit. Keep transport models separate from compiler objects. |
| [SourceDocumentAndDiagnosticTest](xUnitTest/Tests/SourceDocumentAndDiagnosticTest.cs), [ParserRegressionTest](xUnitTest/Tests/ParserRegressionTest.cs) | Extend existing location, source-association, console-output and full-replacement JSON coverage. Add focused server and scheduler tests. |

Prefer small extractions within the existing `Kimi` project. Do not add a new solution project, protocol framework, semantic engine or general plugin architecture for this work.

## 3. Shared analysis design

```text
LSP adapter: JSON-RPC, capabilities, document versions, notifications
                         |
            source updates / immutable snapshots
                         v
Shared analysis service: input identity, scheduling, outcomes, diagnostics
                         |
                         v
Existing project/dependency preparation and compiler front end
                         ^
                         |
CLI check/build/test: reuse preparation and front-end execution

Future CSP adapter -> the same shared analysis service (not implemented here)
```

The LSP adapter owns protocol behavior; the shared layer must not reference LSP DTOs, JSON-RPC, client capabilities or console formatting. CLI callers use the common analysis entry directly without editor debounce. Code generation and test execution stay outside that entry.

Proposed **new internal** types may live under `Kimi/Analysis/`; names are provisional, and existing equivalent types should be extended instead of duplicated:

| Contract | Minimum contents and guarantee |
| --- | --- |
| `AnalysisInput` | Session-local snapshot identity; exact source text and source identity; fixed project configuration, target, language version, product/test mode and dependency inputs. Inputs remain unchanged throughout the attempt. |
| `AnalysisResult` | Input identity, per-phase status, overall outcome, accepted/rejected state, diagnostics and locationless project issues. Distinguish a completed check with errors from cancellation, unresolved inputs and an internal failure. |
| `DiagnosticSnapshot` | Existing diagnostic code, severity, formatted message and source span tied to the captured document; preserve available hints/notes and genuine related locations. No references to mutable compiler collections escape. |
| `AnalysisService` | Prepare and check fixed inputs, return a result, and own attempt-local compiler/diagnostic lifetime. No direct console or protocol output. |
| `AnalysisScheduler` | Coalesce requests by analysis unit, enforce the quiet period, serialize analysis and reject obsolete completions. Inject time and the analysis runner for deterministic tests. |

Use a fresh `Compilation` per attempt initially. Its Binding/ownership state is mutable and is not shared across concurrent requests. Preserve the current sequence: preparation and parsing, Binding and its diagnostics, startup checks, ownership analysis and its control-flow diagnostics, then the existing acceptance checks across source modules. Reuse the checked compilation internally when build or test callers need subsequent work; do not bind a second time.

Collect diagnostics independently of presentation. Keep console output compatible through a renderer/sink and route LSP operational logging to stderr or LSP logging. Project/configuration failures that currently only print text must become structured issues rather than an apparently successful empty result. Do not infer machine-applicable edits from prose in `Fix` or `Note`.

The source/configuration identity and phase outcomes provide a foundation for future verifiable CSP changes. They do not establish native correctness, durable verification evidence, a public CSP schema, or stable AST handles. Retain only active/latest snapshots and published diagnostic state; release obsolete compiler graphs promptly.

## 4. Document and project input model

1. Receive document changes in protocol order and apply every change immediately to the document store. Within one notification, each edit applies to the result of its predecessor. Debounce analysis, not text synchronization.
2. Track document URI, canonical source identity, open lifetime/epoch and LSP version. Reopening with the same version is a new lifetime. Normalize file paths using platform rules and preserve a mapping to client URIs; do not lowercase arbitrary URIs.
3. Keep UTF-16 as the supported LSP position encoding and advertise it where applicable. Use `SourceDocument` conversion for diagnostics. Follow LSP position normalization at the adapter boundary; reject malformed/reversed edits without corrupting the buffer. Ignore duplicate/older document versions with a trace. Test non-BMP text, tabs, EOF and mixed line endings.
4. Capture immutable source text once when an eligible analysis starts; reuse unchanged source snapshots. Preserve exact editor text and avoid flattening or encoding every document on every keystroke. Measure before introducing ropes or semantic caches.
5. Discover project membership from initialization workspace roots and existing project source rules. Reuse production project/target/dependency preparation. If a file belongs to several projects or targets, require a documented session selection; auto-select only an unambiguous project/target. Report ambiguity without silently changing semantics.
6. Replace a disk source with its open-buffer snapshot by canonical identity before compilation. Include the override in the captured dependency input identity as well. Never add a second declaration-bearing copy through `Project.AddSource`. An edited dependency source invalidates every affected active analysis unit.
7. Use standalone analysis only for a file with no project association. Give it an explicit production preparation configuration, initially Library output kind, and report that scope. Do not use `Compilation.CreateForTest` or fall back to standalone mode when a real project is unresolved.
8. Keep product and test modes explicit. Initial automatic project diagnostics use product mode; excluded `TestSources` and inline test bodies must not be reported as checked. If test-mode diagnostics are exposed in this scope, reuse existing test preparation and verify them separately. CLI/LSP parity comparisons use identical inputs and mode.
9. Support save, watched source/configuration/dependency changes and workspace-folder changes through the same invalidation path. Watch only relevant inputs and exclude generated output. Register supported client notifications or use a scoped server watcher when needed; document the fallback. External disk changes cannot overwrite an open overlay.
10. On close, remove the overlay and invalidate its jobs. For a project file, reanalyse using disk content; for a standalone file, retire its diagnostics. Deleted/renamed sources and changed membership also invalidate the relevant unit. Do not restore packages, rewrite locks or change project files during diagnosis.

## 5. Scheduling: wait until typing settles

Use **trailing-edge debounce with a default quiet period of 250 ms**. Expose a documented server option such as `analysisDebounceMs`, using one validated value per session. This is a proposed starting value, to be assessed in editor smoke testing.

- Reset the unit's deadline on every relevant edit: `eligibleAt = lastChange + debounceDelay`. Continuous input keeps postponing analysis; do not force a periodic run through a maximum-wait timer.
- `didOpen` uses the same short delay, allowing bulk file opens to coalesce. Saving cannot bypass an outstanding typing delay. Configuration and dependency changes also invalidate and coalesce the unit.
- A single owner serializes document updates, generation changes and publication decisions. Compiler analysis runs outside the receiving loop, which must remain responsive to later edits, shutdown and cancellation.
- Start with one active compiler analysis per server. Keep only the newest pending request per analysis unit; use a fair ready queue across units. Do not spawn a compiler task per keystroke or retain an unbounded edit backlog for analysis.
- At dispatch, confirm the quiet deadline has expired and capture the current unit generation plus all relevant document lifetimes/versions and configuration/dependency revisions. An edit to file A must invalidate an in-flight result for file B in the same unit.
- If input changes during analysis, mark the run obsolete and request cooperative cancellation. Existing phases may not stop immediately; add safe cancellation boundaries as needed, then await completion and discard obsolete results. Never abandon a task while allowing it to mutate shared state.
- When the worker becomes free, recheck the latest deadline. An earlier timer firing while work was active does not permit analysis during a newer typing burst.
- Return completions to the state owner. Check identity immediately before admitting diagnostics to the output queue; superseded queued publications must be discarded. A response already sent before a later edit is naturally a result for its stated version.
- On close/reopen, workspace removal or shutdown, cancel timers, retire pending requests, invalidate the relevant generation, and drain owned work without subsequent publication.

Example with the default delay: edits at 0, 90 and 180 ms cause one analysis no earlier than 430 ms. If that run is active when another edit arrives at 460 ms, its result is discarded; the replacement starts no earlier than 710 ms and only after the worker is available.

`LspServer` already has an unused `DelayedTaskExecutor` for dumps. First check whether that helper guarantees timer reset, cancellation, disposal and non-overlap. Reuse it behind the scheduler only if deterministic tests prove the required behavior; otherwise replace this unused hook with a small timer-based scheduler using an injectable clock. Do not assume a delayed executor is a trailing-edge debounce.

## 6. LSP transport and publication

- Keep Content-Length framing and source-generated JSON. Test UTF-8 byte lengths, partial reads, multiple frames, valid header ordering, EOF, invalid envelopes and bounded header/payload handling. Preserve the existing output lock; transport writes must not interleave.
- Parse initialization capabilities and workspace information, and advertise only implemented features. Preserve request IDs, distinguish requests from notifications, and apply JSON-RPC/LSP error and lifecycle rules. A successful shutdown includes `result: null`; an error response has `error` and no `result`. Move process exit policy to the command host so in-memory tests can exercise shutdown safely.
- Start with `textDocument/publishDiagnostics`. Send range, severity, stable existing diagnostic code, `source: "kimigayo"` and message. Send supported related information only when it has a genuine compiler-provided location. Include document version when negotiated and known.
- Group diagnostics by the actual diagnostic source document, not `DiagnosticCollection.Name`, which can identify a merged module. Convert spans against the captured source, never the current mutable editor buffer.
- Publish a complete replacement per affected URI and send an empty list to clear a previous diagnostic set once a fresh completed check has removed it. Include previously published URIs in cleanup, even if the new result contains no diagnostics for them.
- Track contributions by analysis unit so clearing one project cannot erase another project's diagnostics for a shared URI. Merge current contributions deterministically and suppress duplicate payloads where useful.
- Do not publish canceled or superseded results. An unresolved project or internal failure must be visibly reported and must not be interpreted as a clean check. Retire invalid source diagnostic contributions and surface the current failure through a project issue; clearing UI state in this case is not verification success.
- Attach a project/configuration issue to a real location only when one exists. Otherwise use a deduplicated LSP message/log and retain the locationless structured issue internally. Never invent line-zero compiler diagnostics to fit the wire format.

## 7. Implementation units and completion conditions

All units are **TODO**. The order deliberately proves a small end-to-end path before broad project integration; no unit authorizes CSP implementation. Commit each verified coherent unit, splitting a row further when necessary.

| Unit | Work | Completion condition |
| --- | --- | --- |
| L1 | Separate diagnostic collection and presentation; produce attempt-local snapshots and structured project issues. | Existing console/caret and diagnostic precision tests pass. Codes, source associations, deduplication and failure history are preserved; repeated attempts cannot leak diagnostics. |
| L2 | Extract common preparation/check execution from `Project.BuildTarget`, adapting CLI callers while retaining emission/test handoff. | Identical inputs produce the same acceptance and diagnostic codes/ranges before and after extraction, including dependency modules and startup/ownership failures. No duplicate Binding or change to product/test partitioning. |
| L3 | Make server streams/lifetime testable; repair protocol responses; preserve exact document text and revision identity. | In-memory initialize/change/shutdown sessions pass. Full/ranged edits, UTF-16, line endings, fragmented framing and `result: null` are covered. No compiler output contaminates stdout. |
| L4 | Connect single-file diagnostics through snapshots and the debounce scheduler, including stale-result guards from the first integration. | Unsaved errors appear and disappear; continuous input postpones analysis; the receive loop stays responsive; obsolete and closed/reopened results never publish. No concurrent compiler runs. |
| L5 | Add project discovery/selection, source overlays, fixed dependency inputs, configuration changes and cross-file publication. | Unsaved A changes diagnostics in B; overlays replace disk sources; close restores disk analysis; shared-project contributions clear correctly; unresolved projects stay visibly unresolved. Saved-input results agree with CLI for the selected scope. |
| L6 | Complete lifecycle/failure/performance checks, run a real editor smoke test, and document configuration and support boundaries. | All acceptance cases below pass, verification evidence is recorded, editor launch settings are confirmed, and STATUS describes only verified support. |

For L2, extract the shared execution entry first and extend input preparation incrementally through L5. L3 can be prepared independently after L1, but diagnostics must not ship through a synchronous receive-loop analysis shortcut. Do not broaden this work into an unrelated compiler refactor.

## 8. Verification and evidence

Extend existing tests first. Proposed **new** classes, only where a distinct fixture is needed: `AnalysisServiceTest`, `LspProtocolTest`, `LspDocumentTest`, `AnalysisSchedulerTest` and `LspProjectDiagnosticTest` under `xUnitTest/Tests`. These names are placeholders until implemented.

| Area | Required cases |
| --- | --- |
| Diagnostics and shared analysis | Parse, Binding, startup, control-flow and ownership failures; independent errors and suppression behavior; merged-file source attribution; dependency errors; global failures; repeated runs; matching CLI acceptance and diagnostic codes/severities/ranges. |
| Text and protocol | Multiple edits in one notification, full replacements, insertion/deletion at EOF, empty files, CRLF/LF/CR, non-BMP characters, path/URI identity, stale versions, close/reopen epochs, framing/lifecycle/error responses and clean stdout. |
| Debounce and races | Fake-clock edits at 0/90/180 ms do not start before 430 ms and start exactly once when eligible and idle; indefinite typing; input during a blocked analysis; timer firing while active; close/reopen; cross-file invalidation; change between completion and publication; shutdown with pending work. |
| Project scope | Source override without duplicate declarations; dependency overlays; target/project ambiguity; product/test exclusions; save/watch/close/delete/config changes; overlapping projects; invalid configuration or unavailable dependencies; recovery after failure and removal of stale diagnostic contributions. |
| Performance and editor behavior | Record analyses started/canceled/discarded and quiet-period-to-publication latency on representative files/projects; verify one active run and bounded retained snapshots. Inspect allocations on edit/snapshot hot paths and at L6. No performance gain is claimed without measurement. |

Use injected fake time and controllable analysis completions for scheduler tests; do not rely on wall-clock sleeps. Use protocol tests for framing and lifecycle plus compiler-backed integration tests for actual diagnostics. A mock analyzer alone cannot establish compiler correctness.

For each implementation unit, run `./verify.ps1 -Class <actual related test classes>` with related fixtures/milestones when compiler changes affect them. Finish an implementation session with `./verify.ps1 -Mode Session [...]`, following [AGENTS.md](AGENTS.md). The script's non-incremental builds are the build evidence. Do not edit sources during verification. Do not run NativeAOT tests.

L6 includes a manual editor run: launch the actual server command, type continuously, pause, introduce/fix errors without saving, change a cross-file declaration, close/reopen files, alter relevant configuration and exit/restart. Locate and verify the actual client configuration; historical KimiCode notes alone are not evidence of a working client.

This planning change requires documentation checks only: Markdown links, `git diff --check`, and the `PLAN.md` line limit. No implementation or build/test result is claimed here.

## 9. Decisions to validate during implementation

| Decision | Initial choice | Validation point |
| --- | --- | --- |
| Quiet period | 250 ms trailing edge; session option; no maximum-wait trigger during typing. | L4 fake-clock tests; L6 perceived latency and workload measurements. |
| Cancellation granularity | Cooperative phase boundaries and mandatory stale-result discard; one worker. | L2/L4 inspect long-running phases; add bounded safe checkpoints if shutdown or latency requires them. |
| Document storage | Retain pooled line storage if exact text can be preserved with a small change. | L3 text round trips and L6 allocation measurements. |
| Project/target selection | Use an unambiguous association or documented explicit session selection. | L5 against actual repository project layouts; do not silently select a different target. |
| Test-region diagnostics | Product mode by default; explicitly scoped test mode only if verified. | L5 mode reporting and CLI parity tests. |
| Related locations and repairs | Preserve available structured locations and textual hints; no invented causal graph or edit actions. | L1/L6 identify what the existing compiler can actually provide. |
| Client integration | Existing server command and confirmed client launch settings. | L6 locate the editor integration and record exact tested versions/settings. |

Next action when implementation is requested: start L1 with diagnostic collection/presentation regression coverage, then extract L2 while preserving the current check/build/test callers. Record completed units and evidence here; keep PLAN.md as a short index and update STATUS.md only when verified support changes.

## 10. Protocol references

- [LSP 3.17 specification](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/specification.md): base protocol, initialization, capabilities and lifecycle.
- [Document changes](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/textDocument/didChange.md): ordered changes and versions.
- [Positions](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/types/position.md): encoding and position normalization.
- [Diagnostic publication](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/language/publishDiagnostics.md): replacement, clearing and version support.
