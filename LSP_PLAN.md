# LSP Compiler Diagnostics Implementation Plan

Status: **Planned; not started.** Based on source inspection at `81dab2f2` (2026-09-27), re-confirmed at `07050505`: none of the §2 files changed. Nothing was executed. Compiler milestones remain in [PLAN.md](PLAN.md).

## 1. Objective and scope

Publish compiler diagnostics, including those for unsaved edits, through the existing LSP server, and extract a check foundation that a future Compiler Server Protocol (CSP) adapter can reuse.

- **In scope:** lifecycle, document synchronization, implicit-project and project checks in product and test modes, all front-end diagnostics (syntax, Binding, startup, control flow, ownership), debounced checking, validity-checked publication and an editor smoke test.
- **Out of scope:** CSP itself, AST inspection or edits, automatic fixes, verification jobs, completion, navigation, formatting, pull diagnostics, incremental compilation, native execution, solution files and non-`file:` URIs.
- **CSP boundary:** `CheckDiagnostic`, outcomes and recorded inputs are designed for CSP reuse. Revisions and content comparisons are session-local; durable content identity, durable evidence, stable AST handles and a public schema remain CSP work.
- **Constraints:** Work stays inside the `Kimi` project: no new solution project, protocol framework, semantic engine or plugin architecture. Language requirements remain in [SPEC.md](SPEC.md) and [IMPLEMENTATION.md](IMPLEMENTATION.md); project semantics remain in [Chapter 18](spec/18-modules-and-dependencies.md).

## 2. Current code

| Area | Finding that drives work |
| --- | --- |
| [LspServer](Kimi/Lsp/LspServer.cs), [LspCommand](Kimi/Unit/Command/LspCommand.cs) | Streams are bound to the console in the constructor. `exit` calls `Environment.Exit` inside the handler. Diagnostic publication is commented out. An unused `DelayedTaskExecutor` dump hook exists. Every send allocates a header string and byte arrays. |
| [TextDocument](Kimi/Lsp/TextDocument.cs), [TextLine](Kimi/Lsp/TextLine.cs) | Per-line pooled objects in a list, which multi-line edits reshuffle; not thread-safe. `ToString()` joins lines with LF, which loses line endings. Out-of-range positions are silently clamped. There is no revision tracking. |
| [Json.cs](Kimi/Lsp/Json.cs), [LspJsonContext](Kimi/Lsp/LspJsonContext.cs) | `WhenWritingNull` omits `result: null`. `LspDiagnostic` lacks `code` and `source`. |
| [Kimigayo](Kimi/Unit/Kimigayo.cs), [DiagnosticCollection](Kimi/Diagnostics/DiagnosticCollection.cs) | The DI-singleton `Kimigayo` keeps collections keyed by project-relative path ([Kotonoha.cs:259](Kimi/Compiler/Core/Kotonoha.cs:259)), so equal paths in different projects collide and old sources stay reachable. `Add` renders to the console while holding its lock and suppresses later diagnostics at a used start offset. `ErrorVersion` still counts suppressed errors. Hints are concatenated into the message. There are no related locations. |
| [Project](Kimi/SolutionAndProject/Project.cs), [Project.Testing](Kimi/SolutionAndProject/Project.Testing.cs) | Discovery takes the top-directory `*.kimi` files at load. `TestSources` are project-relative and may lie elsewhere, such as `tests/…` or `../…` ([§18.8](spec/18-modules-and-dependencies.md#188-product-and-test-inputs)). A project file that fails to load is only logged. `CreateFromSource` registers whatever path it receives as a source. `BuildTarget` reads and parses one file at a time and stops at the first read or encoding failure. Product preparation reports failures through about 20 `WriteLine` sites that return `false`; test preparation throws `InvalidDataException` and accepts only the Windows x64 target. `BuildCore` validates, resolves and reads sources on every call. |
| [Compilation](Kimi/Compiler/Core/Compilation.cs), [Solution](Kimi/SolutionAndProject/Solution.cs) | `Prepare` accepts only the current language version. The target and `KimiOptions.Debug` define the conditional-compilation variables (`os`, `arch`, `pointerWidth`, `debug`, `release`). Binding also reads the target triple ([Binding.Attributes.cs:100](Kimi/Compiler/Binding/Binding.Attributes.cs:100)). A solution affects checks only through `KimiOptions`, which session settings supply. |
| [DependencyResolver](Kimi/SolutionAndProject/DependencyResolver.cs), [DependencyLock](Kimi/SolutionAndProject/DependencyLock.cs) | `Resolve` covers the product and test partitions for one target. It follows product references transitively and adds test references for the root project only. `Load` rescans directories and reads every source from disk. `SameInput` compares raw source bytes, and a mismatch aborts resolution with `ConflictingInput`; the second path is compared but not compiled. `DependencyLock.Validate` returns that resolution diagnostic before reading the lock `<project>.kimi.lock.json`, which holds no source hashes. |
| [SourceDocument](Kimi/Compiler/Core/SourceDocument.cs), [KimiLibrarySources](Kimi/Compiler/Binding/KimiLibrarySources.cs), [TestDefinition](Kimi/Compiler/Binding/TestDefinition.cs) | `SourceDocument` owns spans and UTF-16 conversion (CRLF/LF/CR) and remains the only location implementation. `FromUtf8` strips a BOM, and `IsTestOnly` is a property of the document. Built-in sources use `compiler://Kimi/<version>/<name>`, load on demand and cache error-free tokens. `TestDefinition.Marker` finds `#Test` at the syntax level, and `IsIncluded` limits tests to the project's own module. |

## 3. Common rules

Later sections apply these rules without restating them.

### 3.1 Principle and terms

**Principle: an undetermined fact is never treated as absent, and a determined failure is reported, not held.** A fact is *undetermined* when the current state cannot decide it: an input it rests on has an event after the base, it rests on a discarded item, or the project that would decide it fails to load. For example:

- Undetermined membership does not form an implicit project.
- *Unknown* test presence requires the test unit.
- A unit whose requirement is undetermined does not retire.
- A pending unit does not run and keeps its last result.
- A project that fails to load keeps its units, each with a Blocked result.

| Term | Meaning | Defined in |
| --- | --- | --- |
| Root, candidate, active, reach | Where discovery starts; projects that may own a document; projects that get units; an attempted load | R1 |
| Required unit, retire | A unit the current state needs; leaving that set | R2 |
| Input key, identity, established | What is read; what a check observes; readable and synchronized | R3 |
| Revision, input event, mark | Identity version; numbered change notice; *unverified* until compared | R4 |
| Derived item, valid, held, adopt | Loaded project, prepared unit or result; inputs current; valid except for a mark; accept a worker item | R5 |
| Base, pending | Event number a check is fixed to; a unit that needs an input changed after the base | R7 |
| Report URI, contributor | Where a unit's diagnostics go; units reporting to one URI | R8 |

### 3.2 Projects and units

- **R1 Discovery, membership and activity**
  - A document's role comes from its extension: a *source document* (`.kimi`), a *project document* (`.kimiproj`) or ignored (anything else, including lock files).
  - A project's members are its discovered sources plus its `TestSources`.
  - Each workspace check runs *discovery* on its snapshot:
    1. Load the *roots*: the selected projects, the project of every open project document, and the *candidates* of every open source document. Candidates are the `.kimiproj` files in the document's directory and in every ancestor up to the file-system root.
    2. Mark a loaded project *active* when it is selected, its project document is open, or it has an open member.
    3. Load the product dependencies of active projects transitively, and the test dependencies of active projects together with their product dependencies. A dependency's own test dependencies are not loaded (§18.8), as in `DependencyResolver`. Each project is loaded once.
    4. Repeat steps 2 and 3 until nothing changes. The number of projects is finite, so discovery ends.

    Discovery *reaches* a project when it tries to load it as a root (step 1) or a dependency (step 3), whether or not the load succeeds.
  - Discovery reads only project files and listings, changes no input and raises no input event. Its loaded projects, with their dependency references, are derived items (R5). Membership is recomputed in each check from them.
  - A project whose state cannot be decided:
    - If its file has an event after the base, it keeps its previous state, and its units are pending (base rule, R7). Without a previous state, the membership it would decide is undetermined.
    - If it fails to load, its existing units stay, because the membership it would decide is undetermined, and each gets a Blocked result for the current input. A root without product or test units gets a failed-root unit (R2). A dependency that fails to load is reported by its consumers' Blocked results.
    - These units stay required only while discovery reaches the project. Once it loads again, its units continue as normal units.
  - Membership of an open source document:
    - It belongs to every loaded project that lists it.
    - A candidate that fails to load leaves its membership undetermined.
    - It forms an implicit project (`Project.CreateFromSource`) only when every candidate loaded and none lists it. A file listed only in the `TestSources` of a project that discovery does not reach is therefore implicit until that project is reached, for example by selection. This limit is documented.
  - Dependencies are checked inside active projects' checks and never become active because of it.
- **R2 Units**
  - The *required units* are derived after discovery, and a prepared unit is made only for a required unit, from the loaded projects.
    - Only adopted items change the required units (R5).
    - A unit whose requirement is undetermined stays in the required set with its last result until it is determined.
    - A unit *retires* only when an adopted derivation shows that it is not required.
  - Kinds:
    - **Product units:** one per product target of each active project. A product result reports test presence from a syntax-level scan of the project's own sources with `TestDefinition.Marker`: *yes* when a marker is found; *no* when none is found and every such source parsed without source errors (`Kotonoha.HasSourceErrors`); otherwise *unknown*.
    - **Test unit:** at most one per active project that has a product target. It is required when the project has `TestSources`, or when an adopted product result of this workspace check (checked or reused) reports *yes* or *unknown*. It is decided when the state owner adopts the project's product results (R7). Its target is the first product target, in configured order, that test preparation accepts; without one, the unit is Blocked.
    - **Failed-root unit:** a Blocked unit for a root that fails to load and has no product or test unit, required only while that holds. A selected project therefore reports its failure even with no document open. The unit retires once the project loads or is no longer a root.
  - Product targets:
    - The session target, else the only configured target, else the host target if it is configured. Otherwise there is one Blocked product unit that asks for a selection, and no product target.
    - With the all-targets setting, every configured target.
    - An implicit project uses the `CreateFromSource` rule.
  - A unit's *effective configuration* is its project inputs, target, mode and `Debug` (the CLI default unless set). CLI parity is judged against `kimi check <project> --Target <t>`, or test preparation with `--Target <t>`, under the same configuration.

### 3.3 Inputs and validity

- **R3 Inputs and identity**
  - Every input is read through one *input store*. An *input key* is a kind plus a `SourceIdentity`:
    - A *file*: a source, project or lock file. A missing file is a file whose identity is its absence.
    - A *listing*: a directory with its pattern, `*.kimi` for discovery or `*.kimiproj` for candidates. The two patterns of one directory are separate inputs.
  - Where content comes from:
    - An open document overrides disk for the content and the existence of source and project files. A listing's names are therefore its disk names plus the open documents in that directory that match its pattern, so an unsaved new file joins its project.
    - Lock files always come from disk.
    - Built-in `compiler://` sources come from the compiler.
  - Preparation reads no source content, so a source edit leaves prepared units valid and needs only a new snapshot. Everything that depends on source content runs in the check, in CLI order: the `SameInput` source comparison (recording both compared paths, including the one not compiled), then lock validation, then compilation.
  - An input's *identity* is what a check observes:
    - Whether it is *established*: readable and synchronized (R6). Any read failure leaves an input unestablished.
    - If established: the bytes of a file, the matching names of a listing, or the absence of a missing file.
    - If unestablished: the observed failure, that is, its kind and the message its diagnostic reports. A repeated failure keeps its revision, and a changed cause gives a new one.

    Every comparison uses the whole identity. `SameInput` compares established inputs only: a read failure is reported first, where the current code reads the file (§4.2).
  - An open document's bytes are the file's BOM (if any) followed by the UTF-8 text, so an unchanged overlay reproduces the disk bytes. Its identity is therefore compared by establishment, BOM and text. The bytes themselves are produced only for `SameInput`. Text that cannot be encoded is reported as `InvalidSourceEncoding_Kd`, like invalid UTF-8 on disk.
- **R4 Revisions and comparisons**
  - Every input has a globally monotonic *revision*. It changes exactly when a comparison finds a different identity; nothing else assigns revisions. An input without a retained identity (content or hash) is registered with a new revision when it is compared.
  - *Input events* are document events (open, change, close) and watched events. They are numbered in arrival order, and each input remembers its last event number.
  - An event *marks* inputs unverified:
    - A document change marks its document.
    - A document open or close marks its document and the listing of its directory whose pattern matches it.
    - A watched event marks its file and each listing of the parent directory whose pattern matches the path.
  - Re-validation (worker) compares:
    - every marked input, reading open documents from their base-step strings (R7);
    - every recorded disk input whose timestamp or length changed (for a listing, the directory timestamp);
    - every unestablished input, because recovery may leave timestamp and length unchanged.
  - Retained disk-derived data is reused only while it is known to be current:
    - A listing reuses its retained disk names, merged with the open documents, only when they were read successfully, it has no watched event and its directory timestamp is unchanged. Otherwise it reads the directory again.
    - An open document's BOM is part of its file's disk state. It is read when not retained, and read again when the file has a watched event or a changed timestamp or length. Typing never reads it.
  - The state owner commits all comparisons of a check in one batch. Under the base rule (R7), a comparison of an input with an event after the base is not committed, and the input stays marked for the next check. A committed comparison clears the mark.
  - Without an event or a changed timestamp or length, an established disk input is assumed unchanged. No-op edits, such as a character typed and deleted within one quiet period, therefore give no new revision.
- **R5 Validity, adoption and retention**
  - Validity:
    - Every *derived item* (loaded project, prepared unit, check result) records the inputs it used with their revisions, including those of a reused prepared unit.
    - It is *valid* while none of them has a newer revision or is marked, and *held* while its only defect is a mark.
    - Each input keeps the items that recorded it: a new revision invalidates them at once, and a mark holds them until it clears.
  - Adoption:
    - The state owner *adopts* a new item only if none of its recorded inputs has an event after the base (R7), and registers it in the same step. Otherwise it discards the item. It also discards a unit's result unless the unit is in the adopted required set.
    - Comparisons are committed before the worker reads (R7), so this equals confirming that every recorded revision is current.
    - An input read for the first time is read once per check and shared by all its items; the first adopted item registers its revision.
    - This one check per adopted item is the only scan. Everywhere else, validity is read from the items' flags.
  - Retention:
    - *Current items* are the loaded projects of the current discovery, and the prepared units and results of required units.
    - An input's content is kept while an open document, a current item that recorded it or the running snapshot needs it. `SourceDocument` instances and tokens follow the content they come from. Each revision denotes one content.
    - An item that is no longer current is released together with the inputs that only it retained.
  - The LSP `version` is only a wire field.
- **R6 Synchronization**
  - A notification's changes apply in order, each to the result of the previous one, whatever their version; a non-increasing version is only logged. A character beyond its line end is clamped, as LSP specifies.
  - A change to a document that is not open is logged and ignored until `didOpen`.
  - An inapplicable change (a reversed range, or a line beyond the document) marks the document *desynchronized*. It is then an unestablished input, so every check that reads it is Blocked. A full-text event (`didOpen`, or a change without a range) resynchronizes the document. Both transitions change its identity, even when the text is unchanged.

### 3.4 Checking and publication

- **R7 Roles and scheduling**
  - Roles:
    - The *receive loop* reads frames, parses JSON and enqueues each message.
    - The *state owner* is the single thread that processes that queue in order. It alone changes shared state: documents and their buffers, revisions, marks, event numbers, bases, adopted items, required units and publication decisions. Each step is small, so receiving never stalls.
    - The *worker* reads the snapshot and immutable data. It enqueues comparisons, loaded projects, prepared units, required units and results for the state owner to adopt in arrival order. One worker runs at a time. It waits for the state owner once per check, after re-validation; everything else is adopted without waiting. No exchange grows with the number of inputs.
    - The *sender* serializes and writes frames under the output lock.
  - A workspace check is fixed to its *base*, the latest input event number when it starts. The state owner publishes the inputs with an event after the base as an append-only immutable set, replaced on each event. A unit is *pending* while one of its known inputs, or an input it read in this check, is in that set. Its *known inputs* are its members and the inputs recorded by its prepared unit or previous result.
  - **Base rule:** work that needs an input with an event after the base takes no effect.
    - Authoritative, by the state owner: a comparison of such an input is not committed (R4), and an item that recorded one is discarded at adoption (R5).
    - Early, by the worker reading the set: discovery keeps that project's previous state (R1), and a pending unit is skipped before it starts or when it reads the input. A stale view of the set only wastes work, because adoption still discards the result.
  - At most one workspace check is pending, with one quiet-period deadline. It starts in three steps:
    1. **Base step (state owner):** fix the base, flatten every marked open document into an immutable string, and route all later input to the next check.
    2. **Re-validation (worker, R4):** the state owner commits the comparisons and hands the worker an immutable copy of the items' validity at that point.
    3. **Discovery and derivation (worker):** run discovery (R1), derive the required units, make their prepared units (R2), and return a Blocked result for each required unit that has no prepared unit (the units of projects that fail to load, and failed-root units) unless its last result is still valid.
  - Reads return the state at the base:
    - An open document is read as its string from the base step.
    - A disk input is read as it is now; its content is loaded once per revision.
    - The entry reads all its file inputs before parsing (§4.2), so a skip never interrupts a started compilation.
  - Visiting order: projects with the most recent member event come first, then by project identity; product units come before the test unit.
    - A unit whose reusable result was valid in the validity copy reuses it (R9). Later events cannot change this decision; the base rule covers them.
    - A pending unit is skipped. A skipped product unit also skips its test unit.
    - Every other required unit with a prepared unit is checked. A unit without one has its result from step 3. The worker checks the test unit when its own product results require it; the result is kept only if the adopted product results require the unit too (R5).
  - A started unit check runs to completion; the LSP host never cancels it.
- **R8 Publication**
  - Validity uses recorded inputs (R5); publication uses *report URIs*. A unit's report URIs are the `file:` sources it checked plus the display URIs of its diagnostics (R9). A URI's *contributors* are the required units whose latest results report to it.
  - Updates: the aggregator updates a URI when a contribution to it changes (a result is adopted or a unit retires) or when one of its contributors is released from a hold. It updates no other URI.
  - Condition: a URI is sent only when all its contributors have valid results; the state owner re-reads their validity flags just before sending.
    - A pending unit keeps its held or invalid last result, so its URIs keep the last sent diagnostics until it runs again. A unit with a Blocked result is not pending, so its failure replaces earlier diagnostics.
    - A required unit without a result is not yet a contributor: its diagnostics can only extend a current set, so nothing stale is shown.
  - Payload: a complete replacement holding the union of the contributors' diagnostics for the URI, sorted by `(range, code, severity, message)` with duplicates removed.
  - Suppression:
    - A URI without a sent entry counts as sent empty.
    - A non-empty payload is skipped when it and its version equal the last send.
    - An empty payload is sent only when the last send was non-empty, and the entry is then dropped.
    - Checked state and sent state are kept separately.

### 3.5 Diagnostics

- **R9 Diagnostics and outcomes**
  - One record carries code, severity, message, an optional location (`SourceIdentity`, optionally with a span) and the reporting unit.
  - Outcomes:
    - **Completed:** checked, with or without errors.
    - **Blocked:** an input or the effective configuration could not be established.
    - **Faulted:** an exception inside the compiler. Read failures are Blocked (R3). A Faulted result is published but never reused, so the next workspace check retries it; its recorded inputs may be incomplete.
    - **Cancelled:** CLI cancellation only; never published.

    Blocked and Faulted results carry a diagnostic that explains them.
  - Display placement, in the LSP adapter only; the record keeps its original location:
    - A location without a span is shown at the start of its file.
    - A missing or `compiler://` location is shown at the start of the unit's project file, or of the implicit source.
  - Outcome changes go to `window/logMessage`, never to `showMessage`.
- **R10 Diagnostic collection**
  - Collections belong to the `Compilation`. Per-file collections are keyed by `SourceIdentity` instead of the project-relative path; module and global collections keep their names.
  - Acceptance uses the existing error state (`HasErrors`, `ErrorVersion`, `HasSourceErrors`), never the displayed list. Per-collection start-offset suppression is unchanged.
  - The CLI renders at `Add` through a callback. The LSP host installs none, and its stdout carries only protocol frames.

## 4. Design

### 4.1 Architecture

```text
receive loop -> queue -> state owner (documents, revisions, adoption, aggregator) -> sender
                           |  base step, commits           ^ worker messages
                           v                               |
                         CheckScheduler -> worker: CheckService (per unit) -> shared check entry -> compiler front end
                                                                                  ^
CLI check/build/test (own Compilation; console rendering at Add) -----------------+
```

The check layer never references LSP DTOs, JSON-RPC, client capabilities or console formatting. New internal types live under `Kimi/Checking/`. The names are provisional; they use *check* because *analysis* already names the analyses in `Kimi/Compiler/Analysis/`.

| Type | Responsibility |
| --- | --- |
| `SourceIdentity` | A file path canonicalized with the platform comparer, or a `compiler://` identifier; it also names diagnostic locations. Replaces the comparers that `BuildCore`, `PrepareTests` and `DependencyResolver` build separately. |
| `InputStore` | R3–R6: input keys, identities and text, overlays of content and existence, revisions, event numbers, marks and comparisons, the items that recorded each input, and synchronization state. It holds no membership. |
| `ProjectIndex` | R1: document roles, discovery, loaded projects with their dependency references, membership, load failures and activity. |
| `PreparedUnit` | Made only for a required unit, from loaded projects: unit key, effective configuration, the resolved partition for its target, membership with test-only flags, and the inputs used. |
| `CheckResult` | Outcome, per-phase status, acceptance, test presence (product units), `CheckDiagnostic[]`, and the inputs used with their revisions. Immutable; no compiler object escapes. |
| `CheckService` | For each unit, creates a `Compilation`, runs the shared entry, converts the outcome into a `CheckResult` and releases the compilation. |
| `CheckScheduler` | Implements R7 with an injected clock and runner. |
| Aggregator (LSP adapter) | Implements R8. It keeps the last non-empty sent payload per URI; an empty payload is one shared instance. |

### 4.2 Check entry

- **Entry:** one internal entry, extracted from `BuildCore`, `BuildTarget` and `PrepareTests`, prepares and checks a caller-created `Compilation` in product or test mode.
  - **Reading:** it reads all its file inputs before parsing (R7), keeping each read's content or failure. It then consumes them in the current order and reports a failure where the current code would, so earlier syntax diagnostics and the CLI output stay unchanged. It records the inputs it consumes. Built-in `compiler://` sources are immutable and still load on demand.
  - **Order:** the `SameInput` source comparison and lock validation (R3), then the current sequence: parsing, Binding with its diagnostics, startup checks, ownership analysis with control-flow diagnostics, and acceptance across all source modules.
  - Every preparation failure becomes a diagnostic with a Blocked outcome.
- **Callers:** the CLI keeps its compilation for emission and tests without binding again; emission, native build and test execution stay outside the entry. `CheckService` is the only caller that converts a compilation into a `CheckResult`.
- **Product and test** use separate compilations, because product facts come from product inputs alone (§18.8) and test preparation can fail while the product check succeeds. They share only immutable inputs: text, `SourceDocument` instances per revision and test-only flag, and tokens.
- **Caches:** correctness comes first. Each cache (prepared units, and `SourceDocument` and token reuse on the `KimiLibrarySources` pattern) needs a test showing identical results with and without it, and a measured benefit.
- Machine-applicable edits are never inferred from `Fix` or `Note` prose.

### 4.3 Documents

- Only checks are debounced; text changes apply as they arrive (R6). Documents with non-`file:` URIs are ignored, and roles follow R1.
- **Storage:** one pooled `char[]` buffer per document; `TextLine` is removed.
  - Edits move data within the buffer.
  - Line starts are recomputed after each edit with the `SourceDocument` line-break rule. One definition therefore covers CR, LF and CRLF, including a CR and an LF joined by an edit. Per-line storage would need a second rule for that join, and snapshots flatten the text anyway.
  - Edit time and copy volume are measured at the start, middle and end of large documents. If they matter, line starts are first updated incrementally; a gap buffer comes after that.
- **Snapshots:** only the state owner touches buffers (R7). In the base step it flattens each marked document once into an immutable string, which the worker compares (R4) and checks read. `SourceDocument` instances are cached per revision and test-only flag, and share that string.
- **Positions:** the server advertises UTF-16 and converts diagnostics with `SourceDocument`. Each `file:` URI maps to a `SourceIdentity` without lowercasing.

### 4.4 Disk inputs and session settings

- **Watching:** at `initialized`, if the client supports dynamic registration, the server registers `workspace/didChangeWatchedFiles` once for `**/*.kimi`, `**/*.kimiproj` and `**/*.kimi.lock.json`, and handles the response or failure. A watched event is an input event (R4) and schedules a workspace check.
- **Without watching,** and for inputs outside watched folders, disk changes are found by re-validation at the next workspace check, which a document event triggers. A change that keeps both timestamp and length is then found only when the file is opened or the session restarts. This limit is documented.
- **Read-only:** a check never restores packages, rewrites locks or edits project files.
- **Session settings:** one validated `initializationOptions` object, fixed for the session: `checkQuietPeriodMs`, selected projects, target, all-targets and `Debug`. Selected projects are absolute paths or `file:` URIs; anything else fails validation.

### 4.5 Scheduling details

- **Quiet period:** 250 ms by default; `eligibleAt = lastEvent + quietPeriod`. There is no maximum wait.
  - Events are the input events (R4) and `initialized` when projects are selected. Discovery raises none.
  - `didSave` needs no handling, because an open document already overrides disk.
- **Deadline recheck:** after a workspace check, the scheduler rechecks the deadline, so a timer that fired during the check cannot start a check in the middle of a newer burst.
- **Shutdown:** stops publication, sends the response and retires the pending check. Checks write only check-local state, so `exit` does not wait for a running check.
- **Cancellation:** cooperative checkpoints are added only if measurements require them.
- **`DelayedTaskExecutor`:** reused only if tests prove trailing-edge reset, disposal and non-overlap; otherwise removed.

Example: edits at 0, 90 and 180 ms start one workspace check no earlier than 430 ms. An edit at 460 ms comes after that check's base:

- The unit running at 460 ms completes. If it read the edited source, the state owner discards its result at adoption.
- Later units that need the source are pending and skipped.
- URIs whose contributors all remain valid are published as each unit finishes.
- The next check starts at 710 ms or when the worker becomes free, whichever is later. It re-checks only the invalid and skipped units, starting with the edited project.

During continuous input, the last sent diagnostics remain.

### 4.6 Transport

- **Framing:** Content-Length framing and source-generated JSON, with bounded headers and payloads, serialized into a pooled buffer writer.
- **JSON-RPC:** requests keep their IDs and are distinguished from notifications. A success response carries `result` (including `null`); an error response carries `error` and no `result`. The command host owns the exit policy.
- **Capabilities:** only implemented capabilities are advertised.
- **`publishDiagnostics`:** range, severity, code (the entry name), `source: "kimigayo"`, message, and the version when the document is open. Version handling is optional for clients, so correctness never depends on it.

## 5. Implementation units

All units are **TODO**. The order is L1 → {L2, L3} → L4 → L5a → L5b → L6: L2 and L3 may run in parallel, and L4 needs both. Diagnostics never ship through a synchronous check in the receive loop.

| Unit | Work | Completion condition |
| --- | --- | --- |
| L1 Diagnostic collection | R10: collections owned by `Compilation`, keyed by `SourceIdentity`, rendered at `Add` through a callback. | Console output and caret tests are unchanged. A suppressed error still fails acceptance. Equal relative paths in two projects do not collide. Repeated checks do not leak. |
| L2 Shared check entry | The §4.2 entry, used by the CLI and `CheckService`: R9 outcomes for every failure; `SourceIdentity` (with `compiler://`); a disk-only `InputStore` with R3 keys and identities; test-only flags in `PreparedUnit`; reading all file inputs before parsing; recording of every consumed input; the `SameInput` source comparison and lock validation at check time. No caches. | CLI acceptance, codes, ranges and output are unchanged for product, test and dependency paths. This includes a `SameInput` conflict under an existing lock, and a syntax error in an earlier file combined with a read or encoding failure in a later one. Former text-only failures produce diagnostics. There is no duplicate Binding. Every consumed input is recorded, including both `SameInput` paths. |
| L3 Protocol and documents | Injected streams, the receive loop and queue, the sender, host exit policy, stdout isolation, response fixes, pooled serialization, the single-buffer document and R6. | In-memory sessions pass, covering framing, UTF-16, line endings and `result: null`. R6 behaviour holds. Stdout stays clean. |
| L4 Implicit projects | Document roles and discovery, with undetermined membership for failed candidates; events, marks and batched comparisons (R4); recorded inputs and adoption (R5); the state owner, the post-base set, the base rule, the base step and the re-validation commit (R7); required-unit derivation; per-unit publication. Only implicit projects publish; real-project members publish nothing until L5a. | Unsaved errors appear and disappear. Stale results are discarded at adoption, and current ones appear once input settles. Close/reopen never shows older results. One worker runs at a time and waits once per check, with no exchange per input. Project members, project documents and files under a broken candidate stay unpublished. |
| L5a Project units | Membership with `TestSources` elsewhere and unsaved new files; project documents; product, test and failed-root units, with the test-unit decision at adoption; undetermined requirements; Blocked results for projects that fail to load; target and `Debug` rules; source and project-file overlays; activity and retirement; R9 display placement. | An unsaved edit in A changes B's diagnostics. Closing restores disk content. Test code in a product file gets test-mode diagnostics, and removing the last `#Test` retires the test unit only after an adopted product result confirms it. A broken candidate is Blocked, not implicit. Breaking a loaded project file replaces its diagnostics with the reported failure, even where earlier diagnostics were shown on that file. A broken selected project reports its failure with no document open, and repairing it retires the failed-root unit and clears its diagnostic. Units of a failing project retire once discovery no longer reaches it. Saved inputs match CLI for the same effective configuration. |
| L5b Dependencies and disk inputs | Dependency diagnostics through aggregation; transitive product and test dependency references in discovery; prepared units only for required units; disk comparisons, pattern-keyed listings and re-validation; watching; retention; the prepared-unit cache; selected projects and all targets. | A dependency edit updates the consumer and the dependency's URIs. A new `.kimi`, a new `.kimiproj` or a restored dependency file is detected, and a Blocked unit recovers. A content edit or an unrelated file creation neither re-prepares nor re-checks unaffected units (counted). Loading many dependency projects for the first time adds no check or wait. Later edits load no project again and prepare no unit that is not required. An unchanged project file gets no new revision when compared, and an unreachable project is released together with its inputs. Results are identical with the cache on and off. |
| L6 Completion | Lifecycle and failure cases, performance (token reuse measured first), editor smoke test and configuration documentation. | All §6 cases pass, the client launch settings and `.kimiproj` synchronization are confirmed, and STATUS.md describes only verified support. |

## 6. Verification

### 6.1 Required cases

Extend [SourceDocumentAndDiagnosticTest](xUnitTest/Tests/SourceDocumentAndDiagnosticTest.cs) and [ParserRegressionTest](xUnitTest/Tests/ParserRegressionTest.cs) first. Placeholder new classes: `CheckServiceTest`, `CheckSchedulerTest`, `LspProtocolTest`, `LspDocumentTest` and `LspProjectDiagnosticTest`.

- **Check**
  - Failures in each phase; merged-module attribution; dependency errors.
  - `SameInput`: matching, then conflicting after an edit to one path, then matching again, with the cache on and off; a conflict under an existing lock reporting the CLI's code and message; an open document whose file has a BOM compared with a copy without one, with the same result on first open and with retained content.
  - Independent diagnostics at one position under the existing suppression; errors that fail acceptance while hidden.
  - Outcomes: a read failure that recovers without a timestamp or length change; a failure that continues with the same cause (reused) or with a changed cause (new revision and diagnostic); earlier syntax errors kept when a later file fails to read or decode; a Faulted unit retried by the next check.
  - Test presence *yes*, *no* and *unknown*, including a syntax error and a semantic error; repeated checks.
  - Target and `Debug` effects; the test target with several configured targets; no test unit without a product target; CLI parity (R2).
- **Documents and protocol**
  - Text: multi-edit notifications, full replacement, EOF and empty files, CRLF/LF/CR, a CR joined to an LF by an edit, non-BMP text, text that cannot be encoded.
  - Revisions: a same-text replacement, and a character typed and deleted within one quiet period, giving no new revision or re-check; the same text with a different BOM giving one, including a close, a BOM-only disk change and a reopen within one quiet period; typing that reads no BOM; a document change marking no listing.
  - Synchronization: a non-increasing version still applied; a change to an unopened document ignored; desynchronization blocking dependent checks; resynchronization, including a reopen with unchanged text that replaces the Blocked result.
  - Protocol: framing, lifecycle, clean stdout; receiving continues while the state owner adopts and publishes; worker messages are adopted in arrival order.
- **Scheduling and adoption** (fake clock, controllable runner)
  - Timing: the §4.5 example, continuous input, a timer firing during a check, the most recently edited project visited first.
  - Base:
    - Input after the base never reaches any unit, including an edit that arrives during re-validation or discovery.
    - An edit after the base step but before its comparison: the comparison is not committed, the mark stays, and the next check flattens the new text.
    - A project file changed on disk is used by discovery in the same check once re-validation commits it. A project file with an event after the base keeps its previous state.
    - A dependency source opened and edited during a check before its first read makes the unit pending and skipped.
    - The worker skips early from the post-base set; with a stale view of the set, adoption still discards the result.
  - Adoption:
    - An edit just before a result completes, and one just before it is adopted: the stale result is discarded.
    - Reuse decided from the validity copy, unchanged by events that arrive during the check.
    - A discarded product result or discovery item changes no unit: the affected units stay with their last results.
    - A result of a unit outside the adopted required set is discarded, including a test result that the adopted product results do not require.
    - An input read for the first time is registered with its item, and one such input shared by two units of a check gets one revision. One wait per check and no exchange per input.
    - Pending units, including a new unit whose member changed after the base; reuse of valid results.
- **Publication**
  - Per-unit sends; a cross-file edit invalidating another URI; only changed or released URIs updated.
  - A pending unit keeping the last sent diagnostics, and a failing project's Blocked units replacing them.
  - A first check with a new test unit (product diagnostics first, test diagnostics added); adding and removing the last `#Test`, with the resulting units and send order; a skipped product unit keeping the existing test unit, which retires only after the next adopted product result confirms the removal.
  - Placed diagnostics (no location, or `compiler://`) published and cleared through report URIs; no send to a `compiler://` URI.
  - Retirement releasing held URIs; lost contributors clearing a URI.
  - No re-send when only the unit order changes; no empty send to a clean file after unrelated edits; skipping an unchanged payload and version.
  - Close/reopen; shutdown with a running check.
- **Membership and inputs**
  - Documents: only a `.kimiproj` open (its project becomes active and is never parsed as source); breaking a loaded project file that already shows diagnostics (the failure replaces them), repairing it, and closing its documents (its units retire); closing a project document; an open lock file ignored.
  - Roots: a broken selected project with no document open reports a Blocked diagnostic. Once repaired while still selected, it recovers without any document opening, and the failed-root unit leaves the required set with its diagnostic cleared. The units of a failing project retire once discovery no longer reaches it.
  - Dependencies: `A → B → C` with only A active loads C; a dependency's own `TestDependencies` are not loaded; a failing dependency is reported by its consumer.
  - Membership: `TestSources` in subdirectories, and in a sibling directory joining its project within the check that reaches it; an unsaved new `.kimi` in a project directory joining the project (saving it keeps its diagnostics, closing it unsaved removes it, and opening a file already on disk changes no listing revision); one file with different test-only flags in two projects; ancestor projects outside the workspace; a broken candidate; multiple memberships; implicit projects; activity and retirement.
  - Content: overlays without duplicate declarations, overlay bytes with and without a BOM (including `SameInput`), opening an unchanged file without a re-check.
  - Disk changes:
    - Events holding publication until compared; an event during a comparison keeping the mark.
    - A created file invalidating only the listing whose pattern matches it, with one directory used for both patterns; a watched creation or deletion detected while the directory timestamp is unchanged; an unrelated file creation causing no new revision.
    - A watched change with equal timestamp and length; re-validation without watching.
    - An unchanged project file of a loaded candidate compared without a new revision; release of an unreachable project together with the inputs only it retained; recovery from Blocked.
- **Performance** (no gain is claimed without measurement)
  - Units checked, reused, skipped and discarded per workspace check; preparations per edit; items invalidated per input change; worker waits and exchanges per check.
  - Loads, preparations and retained memory at the first check, over later edits and after a root becomes inactive, including large `TestDependencies` without a test unit; checks and waits when many dependency projects load for the first time.
  - Latency from the quiet period to publication, with and without a test unit; re-validation cost, including ancestor listings; base-step cost for large open documents; test-mode and all-target cost.
  - Edit time and copy volume at the start, middle and end of large documents; allocations on edit, snapshot and send paths and at L6.
  - Retained memory staying bounded while different projects are opened and closed repeatedly.

### 6.2 Procedure

- Scheduler tests use injected time, never wall-clock sleeps. Compiler correctness requires compiler-backed integration tests; a mock runner cannot establish it.
- Each unit runs `./verify.ps1 -Class <related classes>` with related fixtures or milestones. A session ends with `./verify.ps1 -Mode Session [...]` ([AGENTS.md](AGENTS.md)). NativeAOT tests are not run.
- L6 includes a manual run of the real server command, covering continuous typing and pauses, unsaved errors and fixes, a cross-file declaration change, close/reopen, a configuration change and a restart. Historical KimiCode notes are not evidence of a working client.

## 7. Decisions to validate

| Decision | Initial choice | Fallback if measurements require | Validated in |
| --- | --- | --- | --- |
| Quiet period | 250 ms, session setting | Adjust the default | L4 tests; L6 perceived latency |
| No cancellation | Run to completion and discard stale results at adoption | Cooperative checkpoints between phases | L4/L6 check durations |
| Publication waits for all contributors | No stale mix and no flicker; latency is about product plus test time in projects with tests | Send the valid contributors' union while the others are pending in the same check, and resend when they finish; test-only diagnostics may briefly disappear | L5a/L6 latency with test and dependent units |
| Comparisons | Events mark inputs; the worker compares them at re-validation, with open documents taken from base-step strings and disk inputs checked by timestamp, length and marks | First, query timestamps and lengths in parallel; the state owner still commits once. Then, with a successful watch registration, skip timestamp checks inside the watched folders; this requires tracking workspace folders and watching folder deletion, which the file patterns do not match | L5b/L6 re-validation and base-step cost |
| Single target by default | All targets only by session setting | Share one check between targets only if every target-observable input, including the triple read by Binding, is shown identical | L5a/L6 cost |
| Document storage | One pooled buffer; line starts recomputed per edit | Incremental line starts, then a gap buffer | L3 round trips; L6 edit time and copy volume |
| Retention of closed disk inputs | Keep the content of every input a current item recorded | Keep only a 128-bit hash as the retained identity, and re-read content when a check needs it; a hash mismatch on re-read counts as a change | L6 memory with large dependency sets, together with token reuse |

## 8. Records and references

- **Records:** unit status (TODO/DONE and commit) is kept here. Evidence lives in commits and `bin/verify/`, session lines in [PLAN_HISTORY.md](PLAN_HISTORY.md). STATUS.md changes only when verified support changes.
- **LSP 3.17:**
  - [Specification](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/specification.md): base protocol, initialization, capabilities and lifecycle.
  - [Document changes](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/textDocument/didChange.md): ordered changes and versions.
  - [Positions](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/types/position.md): encoding and normalization.
  - [Diagnostic publication](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/language/publishDiagnostics.md): replacement, clearing and versions.
  - [Watched files](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/workspace/didChangeWatchedFiles.md): registration and file events.
