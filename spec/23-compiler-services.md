# 23. Compiler services

[Specification index](../SPEC.md)

This chapter defines how tools obtain checking results from the compiler: the **check foundation** shared by the commands, the **Language Server Protocol** (LSP) server `kimi lsp`, and the preparatory contracts of the future **Compiler Server Protocol** (CSP). These services never change language validity. A check reports what the equivalent command reports for the same inputs, and editor diagnostics are advisory: they are never build evidence or input records (§18.5.2).

## 23.1. Architecture

```text
editor ──stdio──► receive loop ──► queue ──► state owner ──► sender ──► editor
                                              │      ▲
                               base step,     │      │ comparisons, projects,
                               commits        ▼      │ units, results
                                             worker ──► check entry ──► compiler front end
                                                             ▲
kimi check / kimi test ──────────────────────────────────────┘
future CSP adapter ─────► check foundation (§23.3)
```

| Role | Responsibility |
| --- | --- |
| Receive loop | Reads frames, parses JSON and enqueues each message. |
| State owner | The single thread that processes the queue in order. It alone changes shared server state: documents and their buffers, revisions, marks, event numbers, bases, adopted results, required units and publication decisions. Each step is small, so receiving never stalls. |
| Worker | Runs checks on immutable snapshots and enqueues their products. One worker runs at a time. |
| Sender | Serializes and writes frames under the output lock. |
| Check entry | The one check path shared by the commands and the server (§23.3.2). |

## 23.2. Principle and terms

**Principle: an undetermined fact is never treated as absent, and a determined failure is reported, not held.** A fact is *undetermined* when the current state cannot decide it: an input it rests on has an event after the base (§23.4.6), it rests on a discarded result, or the project that would decide it fails to load. For example:

- Undetermined membership does not form an implicit project.
- *Unknown* test presence requires the test unit.
- A unit whose requirement is undetermined does not retire.
- A pending unit does not run and keeps its last result.
- A project that fails to load keeps its units, each with a Blocked result.

| Term | Meaning | Defined in |
| --- | --- | --- |
| Check unit | One project, target, mode and `Debug` setting checked together | [§23.3.1](#2331-check-units-and-effective-configuration) |
| Outcome | Completed, Blocked, Faulted or Cancelled | [§23.3.3](#2333-outcomes-and-diagnostic-records) |
| Input key, identity, established | What is read; what a check observes; readable and synchronized | [§23.3.4](#2334-inputs-and-identity) |
| Revision, recorded input | Identity version; an input a result used, with its revision | [§23.3.5](#2335-revisions-and-recorded-inputs) |
| Root, candidate, active, reach | Where discovery starts; projects that may own a document; projects that get units; an attempted load | [§23.4.3](#2343-discovery-membership-and-activity) |
| Required unit, retire | A unit the current state needs; leaving that set | [§23.4.4](#2344-units-and-retirement) |
| Input event, mark, held | Numbered change notice; *unverified* until compared; valid except for a mark | [§23.4.5](#2345-change-detection) |
| Base, pending, adopt | Event number a workspace check is fixed to; a unit that needs an input changed after it; accept a worker result | [§23.4.6](#2346-scheduling-and-consistency) |
| Report URI, contributor | Where a unit's diagnostics go; the units reporting to one URI | [§23.4.7](#2347-publication) |

## 23.3. Check foundation

### 23.3.1. Check units and effective configuration

A **check unit** checks one project in one mode:

- **Product mode** checks the product inputs. Its result reports *test presence* from a syntax-level scan of the project's own sources for `#Test` functions: *yes* when one is found; *no* when none is found and every such source parsed without source errors; otherwise *unknown*.
- **Test mode** runs test preparation (§20.9) without discovery or execution.

A unit's **effective configuration** is its project inputs, target, mode and `Debug` setting. A result must equal what `kimi check <project> --Target <t>` reports, or what test preparation with `--Target <t>` reports, under the same effective configuration and saved inputs. Product and test units use separate compilations, because product facts come from product inputs alone (§18.8) and test preparation can fail while the product check succeeds.

### 23.3.2. Check entry

One check entry serves `kimi check`, test preparation and the server. For each unit it prepares and checks one compilation:

1. **Reading.** It reads every file input before parsing and keeps each read's content or failure. It then consumes them in the command's order and reports a failure where the command would, so earlier syntax diagnostics and command output stay unchanged. Built-in `compiler://` sources are immutable and load on demand.
2. **Dependency content.** The *same-input comparison* of dependency nodes reached by several paths (§18.4.1), then lock validation (§18.5), both in command order.
3. **Front end.** Parsing, Binding and its diagnostics, startup checks, ownership analysis with control-flow diagnostics, and acceptance across all source modules.

Every preparation failure becomes a diagnostic with a Blocked outcome. A command keeps its compilation for emission and tests without binding again; emission, native builds and test execution lie outside the entry. Machine-applicable edits are never inferred from `Fix` or `Note` prose.

### 23.3.3. Outcomes and diagnostic records

One **diagnostic record** carries the code (the entry name), severity, message, an optional location (a source identity, optionally with a range) and the reporting unit. A result has one **outcome**:

| Outcome | Meaning |
| --- | --- |
| Completed | Checked, with or without errors. |
| Blocked | An input or the effective configuration could not be established, including every read failure. |
| Faulted | An exception inside the compiler. Its recorded inputs may be incomplete; it is published but never reused, so the next check retries it. |
| Cancelled | Command cancellation only; never published. |

Blocked and Faulted results carry a diagnostic that explains them.

**Diagnostic collection.** Diagnostic collections belong to one compilation. Records are attributed to documents by their source document's identity, never by a collection name. Acceptance uses the compiler's error state, never the displayed list, and per-collection start-offset suppression is unchanged. Commands render each diagnostic when it is added; the server renders none, and its standard output carries only protocol frames.

### 23.3.4. Inputs and identity

Every input is read through one input store. An **input key** is a kind plus a source identity:

- A **file**: a source, project or lock file. A missing file is a file whose identity is its absence.
- A **listing**: a directory with its pattern, `*.kimi` for discovery or `*.kimiproj` for candidates. The two patterns of one directory are separate inputs.

A **source identity** is a file path canonicalized with the platform comparer, or a `compiler://` identifier. Where content comes from:

- An open document overrides disk for the content and the existence of source and project files. A listing's names are its disk names plus the open documents in that directory that match its pattern, so an unsaved new file joins its project.
- Lock files always come from disk; built-in `compiler://` sources come from the compiler.

Preparation reads no source content, so a source edit needs only a new snapshot. An input's **identity** is what a check observes:

- Whether it is **established**: readable and synchronized (§23.4.2). Any read failure leaves an input unestablished.
- If established: the bytes of a file, the matching names of a listing, or the absence of a missing file.
- If unestablished: the observed failure, that is, its kind and the message its diagnostic reports. A repeated failure keeps its identity, and a changed cause changes it.

Every comparison uses the whole identity. The same-input comparison compares established inputs only; a read failure is reported first, where the command reads the file. An open document's bytes are its file's BOM (if any) followed by the UTF-8 text, so an unchanged overlay reproduces the disk bytes. Its identity is therefore compared by establishment, BOM and text. Text that cannot be encoded is reported as `InvalidSourceEncoding_Kd`, like invalid UTF-8 on disk.

### 23.3.5. Revisions and recorded inputs

Every input has a globally monotonic **revision**, which changes exactly when a comparison finds a different identity. An input without a retained identity gets a new revision when it is compared. Every result records the inputs it used with their revisions, including the uncompiled path of a same-input comparison. Revisions and comparisons are session-local; durable content identity belongs to the CSP (§23.5).

## 23.4. Language Server Protocol

### 23.4.1. Transport and lifecycle

- **Framing:** `Content-Length` headers and UTF-8 JSON bodies, with bounded headers and payloads.
- **JSON-RPC:** requests keep their IDs and are distinguished from notifications. A success response carries `result`, including `null`; an error response carries `error` and no `result`. An unknown request receives `-32601`; an unknown notification is ignored.
- **Capabilities:** only implemented capabilities are advertised: incremental `textDocumentSync` with open and close notifications, and the UTF-16 position encoding.
- **Lifecycle:** `shutdown` stops publication, answers and retires the pending check; `exit` ends the process with code 0 after `shutdown` and 1 otherwise, without waiting for a running check. The command host owns the exit.

### 23.4.2. Documents and synchronization

- A document's role comes from its extension: a **source document** (`.kimi`), a **project document** (`.kimiproj`), or ignored (anything else, including lock files). Documents with non-`file:` URIs are ignored.
- A notification's changes apply in order, each to the result of the previous one, whatever their version; a non-increasing version is only logged. A character beyond its line end is clamped, as LSP specifies. Positions are UTF-16 code units, and line breaks follow the source rule (CR, LF and CRLF).
- A change to a document that is not open is ignored until `didOpen`.
- An inapplicable change (a reversed range, or a line beyond the document) makes the document **desynchronized**: it is then an unestablished input, so every check that reads it is Blocked. A full-text event (`didOpen`, or a change without a range) resynchronizes it. Both transitions change its identity, even when the text is unchanged.
- Only checks are debounced; text changes apply as they arrive. `didSave` needs no handling, because an open document already overrides disk.

### 23.4.3. Discovery, membership and activity

A project's members are its discovered sources plus its `TestSources`. Each workspace check runs **discovery** on its snapshot:

1. Load the **roots**: the selected projects, the project of every open project document, and the **candidates** of every open source document. Candidates are the `.kimiproj` files in the document's directory and in every ancestor up to the file-system root.
2. Mark a loaded project **active** when it is selected, its project document is open, or it has an open member.
3. Load the product dependencies of active projects transitively, and the test dependencies of active projects together with their product dependencies. A dependency's own test dependencies are not loaded (§18.8). Each project is loaded once.
4. Repeat steps 2 and 3 until nothing changes.

Discovery **reaches** a project when it tries to load it as a root or a dependency, whether or not the load succeeds. Discovery reads only project files and listings, changes no input and raises no event.

- **Membership of an open source document.** It belongs to every loaded project that lists it. A candidate that fails to load leaves its membership undetermined. It forms an implicit project (§20.8.6.1) only when every candidate loaded and none lists it. A file listed only in the `TestSources` of a project that discovery does not reach is therefore implicit until that project is reached, for example by selection.
- **Undeterminable project state.** A project whose file has an event after the base keeps its previous state and units (§23.4.6); without a previous state, the membership it would decide is undetermined. A project that fails to load keeps its existing units, each with a Blocked result; a root without product or test units gets a failed-root unit; a dependency that fails to load is reported by its consumers' Blocked results. These units stay required only while discovery reaches the project.
- Dependencies are checked inside active projects' checks and never become active because of it.

### 23.4.4. Units and retirement

The **required units** are derived after discovery. Only adopted results change them. A unit whose requirement is undetermined stays required with its last result, and a unit **retires** only when an adopted derivation shows that it is not required.

| Unit | Exists |
| --- | --- |
| Product | One per product target of each active project. |
| Test | At most one per active project that has a product target, when the project has `TestSources` or an adopted product result of the current check reports *yes* or *unknown*. Its target is the first product target, in configured order, that test preparation accepts; without one it is Blocked. |
| Failed root | One Blocked unit for a root that fails to load and has no product or test unit, while that holds. A selected project therefore reports its failure with no document open. |

**Product targets** are the session target, else the only configured target, else the host target if it is configured; otherwise one Blocked product unit asks for a selection. The all-targets setting selects every configured target. An implicit project uses its own target rule (§20.8.6.1).

### 23.4.5. Change detection

- **Input events** are document events (open, change, close) and watched-file events. They are numbered in arrival order, and each input remembers its last event number.
- An event **marks** inputs unverified. A document change marks its document; a document open or close also marks the listing of its directory whose pattern matches it; a watched event marks its file and each listing of the parent directory whose pattern matches the path.
- A result whose recorded inputs have no newer revision and no mark is **valid**; one whose only defect is a mark is **held**. A new revision invalidates every result that recorded the input, at once.
- **Re-validation** compares every marked input, every recorded disk input whose timestamp or length changed (for a listing, the directory timestamp), and every unestablished input, because recovery may leave timestamp and length unchanged. Retained disk data is reused only while known to be current: a listing's names only when they were read successfully, it has no watched event and its directory timestamp is unchanged; an open document's BOM only while its file has no watched event and no timestamp or length change. Typing never reads the disk.
- All comparisons of a check are committed together. A committed comparison clears the mark; a comparison of an input with an event after the base is not committed, and the mark stays for the next check.
- Without an event or a changed timestamp or length, an established disk input is assumed unchanged. No-op edits, such as a character typed and deleted within one quiet period, therefore cause no recheck. This freshness limit applies to editor diagnostics only; commands always read the actual bytes (Appendix D.3).

### 23.4.6. Scheduling and consistency

- **Quiet period.** At most one workspace check is pending, eligible at `lastEvent + quietPeriod` (250 ms by default). Every input event resets the deadline, and there is no maximum wait. `initialized` also schedules a check when projects are selected.
- **Base.** A workspace check is fixed to its **base**, the latest input event number when it starts. A unit is **pending** while one of its known inputs, or an input it read in the check, has an event after the base; its known inputs are its members and the inputs recorded by its prepared unit or previous result.
- **Base rule.** Work that needs an input with an event after the base takes no effect: its comparison is not committed, discovery keeps that project's previous state, a pending unit is skipped, and a result that recorded such an input is discarded at adoption. The state owner decides this; the worker may skip early from a published set of changed inputs, and a stale view of that set only wastes work.
- **Start.** (1) The state owner fixes the base, takes an immutable copy of every changed open document and routes later input to the next check. (2) The worker re-validates, and the state owner commits the comparisons. (3) The worker runs discovery, derives the required units and returns a Blocked result for each required unit without a prepared unit, unless its last result is still valid.
- **Visiting.** Projects with the most recent member event come first, then by project identity, with product units before the test unit. A unit whose result was valid when the comparisons were committed reuses it; a pending unit is skipped, and its test unit with it; every other required unit is checked. A started check runs to completion; the server never cancels it.
- **Adoption.** The state owner adopts a result only if none of its recorded inputs has an event after the base and its unit is in the adopted required set; otherwise it discards the result. A test result is kept only if the adopted product results require the test unit.
- **Retention.** Content and derived data are kept only while an open document, a current discovery, a required unit or the running check needs them; a project that discovery no longer reaches is released with the inputs only it retained.

Example: edits at 0, 90 and 180 ms start one workspace check no earlier than 430 ms. An edit at 460 ms comes after that check's base. The unit running at 460 ms completes, and its result is discarded if it read the edited source; later units that need the source are skipped; URIs whose contributors all remain valid are published as each unit finishes. The next check starts at 710 ms or when the worker becomes free, whichever is later, and rechecks only the invalid and skipped units, starting with the edited project.

### 23.4.7. Publication

- **Report URIs.** A unit's report URIs are the `file:` sources it checked plus the display URIs of its diagnostics. A URI's **contributors** are the required units whose latest results report to it.
- **Display placement.** A location without a range is shown at the start of its file. A missing or `compiler://` location is shown at the start of the unit's project file, or of the implicit source. The record keeps its original location.
- **Updates.** A URI is reconsidered when a contribution to it changes (a result is adopted or a unit retires) or when a contributor is released from a hold, and at no other time.
- **Condition.** A URI is sent only when all its contributors have valid results. A pending unit keeps its last result, so its URIs keep the last sent diagnostics until it runs again. A Blocked result is not pending, so a failure replaces earlier diagnostics. A required unit without a result is not yet a contributor; its diagnostics can only extend a current set.
- **Payload.** `textDocument/publishDiagnostics` carries a complete replacement: the union of the contributors' diagnostics for the URI, sorted by range, code, severity and message, without duplicates. Each diagnostic has a range, severity, code, `source: "kimigayo"` and message, and the notification carries the document version when the document is open. Version handling is optional for clients, so correctness never depends on it.
- **Suppression.** A URI never sent counts as sent empty. A non-empty payload equal to the last send, with the same version, is not sent again, and an empty payload is sent only after a non-empty one.
- Changes of an outcome are logged with `window/logMessage`, never shown with `window/showMessage`.

### 23.4.8. Watching and session settings

- **Watching.** At `initialized`, when the client supports dynamic registration, the server registers `workspace/didChangeWatchedFiles` once for `**/*.kimi`, `**/*.kimiproj` and `**/*.kimi.lock.json`. Without watching, and for inputs outside watched folders, disk changes are found by re-validation at the next check that a document event starts; a change that keeps both timestamp and length is then found only when the file is opened or the session restarts.
- **Read-only.** A check never restores packages, rewrites locks or edits project files.
- **Settings.** `initializationOptions` is validated once and fixed for the session. Unknown members are ignored with a log message; an invalid value is rejected with a log message and replaced by its default.

| Member | Default | Meaning |
| --- | --- | --- |
| `checkQuietPeriodMs` | `250` | Quiet period, 0–10000. |
| `selectedProjects` | `[]` | Absolute paths or `file:` URIs of `.kimiproj` files to treat as roots. |
| `target` | none | The target of product units (§23.4.4). |
| `allTargets` | `false` | Checks every configured target. |
| `debug` | `false` | The `Debug` setting of every unit. |

### 23.4.9. Example session

```text
→ {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"initializationOptions":{"checkQuietPeriodMs":200}}}
← {"jsonrpc":"2.0","id":1,"result":{"capabilities":{"positionEncoding":"utf-16","textDocumentSync":{"openClose":true,"change":2}},"serverInfo":{"name":"Kimi Language Server","version":"…"}}}
→ {"jsonrpc":"2.0","method":"initialized","params":{}}
→ {"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/work/Hello.kimi","languageId":"kimi","version":1,"text":"let x = 1 +\n"}}}
   (quiet period, then one workspace check)
← {"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///C:/work/Hello.kimi","version":1,"diagnostics":[{"range":{"start":{"line":0,"character":10},"end":{"line":0,"character":11}},"severity":1,"code":"…_Kd","source":"kimigayo","message":"…"}]}}
→ {"jsonrpc":"2.0","id":2,"method":"shutdown"}
← {"jsonrpc":"2.0","id":2,"result":null}
→ {"jsonrpc":"2.0","method":"exit"}
```

## 23.5. Compiler Server Protocol

**Design status: specified, not implemented** (Appendix D).

### 23.5.1. Purpose

The CSP will give programs, including AI agents, a structured interface to the compiler:

- **Diagnostics:** causes, related locations and repair candidates with explicit preconditions and guarantees.
- **Syntax inspection and manipulation:** inspect syntax and semantics, apply edits against identified source snapshots, and produce reviewable source changes.
- **Verifiable changes:** associate checks, tests and measurements with exact source and configuration, and report their outcomes and remaining uncertainty.

### 23.5.2. Provided foundation

The check foundation (§23.3) is the CSP's base: the check entry, check units with their effective configuration, outcomes, diagnostic records and recorded inputs. The CSP adapter uses them exactly as the language server does. They are session-local and carry no public schema.

### 23.5.3. Requirements

When the CSP is introduced, it must:

- identify every source snapshot by durable content identity, so that edits, results and evidence name exactly the inputs they rest on;
- apply edits only against an identified snapshot and return them as reviewable source changes, never as silent file writes;
- offer repair candidates only as structured edits with stated preconditions and guarantees, never inferred from `Fix` or `Note` prose;
- give stable handles to syntax and semantic nodes within a snapshot;
- bind every check, test and measurement to its exact source and configuration, and report its outcome and any remaining uncertainty;
- keep the principle of §23.2 and the outcomes of §23.3.3, and never narrow a language rule.
