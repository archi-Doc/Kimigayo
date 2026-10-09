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
| Sender | Serializes and writes frames in order, one at a time; the state owner never waits for output. |
| Check entry | The one check path shared by the commands and the server (§23.3.2). |

## 23.2. Principle and terms

The [virtual semantic records](virtual-dispatch.md#7-semantic-records-and-diagnostics) and the selected [object adaptation](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) are shared by diagnostics, Hover and CSP inspection. Object adaptations report the existing cause-specific Type/ObjectPayload, acquisition, ownership-mode and Loan failures at the written operation, retaining input, result and dependency facts. A Move repair requires verified Take and post-acquisition conditions; failure never reselects another operation.

**Principle: an undetermined fact is never treated as absent, and a determined failure is reported, not held.** A fact is *undetermined* when the current state cannot decide it: an input it rests on has an event after the base (§23.4.6), it rests on a discarded result, or the project that would decide it fails to load. For example:

- Undetermined membership does not form an implicit project.
- *Unknown* test presence requires the test unit.
- A unit whose requirement is undetermined does not retire.
- A pending unit does not run and keeps its last result.
- A project that fails to load keeps its units, each with a Blocked result.

| Term | Meaning | Defined in |
| --- | --- | --- |
| Check unit | One project, target, mode and `Debug` setting checked together | [§23.3.1](#2331-check-units-and-effective-configuration) |
| Outcome, accepted | Completed, Blocked, Faulted or Cancelled; whether the checked program passes | [§23.3.3](#2333-outcomes-and-acceptance) |
| Diagnostic record, category | One published problem with its explanation; the kind of problem a code reports | [§23.3.6](#2336-diagnostics) |
| Problem, prerequisite, derived | One failed requirement of one subject; a requirement another depends on; a requirement left undecided by a failed prerequisite | [§23.3.6.4](#23364-problems-prerequisites-and-suppression) |
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

1. **Reading.** It reads every file input before parsing and keeps each read's content or failure. It then consumes them in the command's order and reports a failure where the command would, so the check reports what the command reports. Built-in `compiler://` sources are immutable and load on demand.
2. **Dependency content.** The *same-input comparison* of dependency nodes reached by several paths (§18.4.1), then lock validation (§18.5), both in command order.
3. **Front end.** Parsing, Binding and its diagnostics, startup checks, ownership analysis with control-flow diagnostics, and acceptance across all source modules.

Every preparation failure becomes a diagnostic with a Blocked outcome. A command keeps its compilation for emission and tests without binding again; emission, native builds and test execution lie outside the entry. Machine-applicable edits are never inferred from `Advice` or `Note` prose: a record offers them only as repair candidates (§23.3.6.9).

### 23.3.3. Outcomes and acceptance

A result has one **outcome**:

| Outcome | Meaning |
| --- | --- |
| Completed | Checked, with or without errors. Language errors, unproven facts and unsupported forms reject a Completed result. |
| Blocked | An input or the effective configuration could not be established, including every read failure. An `Input` Error explains it. |
| Faulted | An exception inside the compiler, or a violation of the diagnostic contract (§23.3.6.7). Its recorded inputs may be incomplete; it is published but never reused, so the next check retries it. |
| Cancelled | Command cancellation only; never published. |

The outcome follows from how the check ended, never from the categories of its diagnostics. A check that reports `InternalInvariant_Kd` where analysis can continue safely is Completed and rejected.

**Faulted.** Exactly one `CheckFaulted_Kd` Error explains a Faulted result. Its Reason carries the fault kind, and its fixed text depends on no catalog. When analysis throws while diagnostic collection is intact, the result keeps its valid records and adds that Error. When a diagnostic contract is violated, or collection or finalization itself fails, the partial records are discarded.

A result is **accepted** only when all of the following hold:

1. Its inputs, effective configuration, dependency content and lock validation are established.
2. Every check of §23.3.2 step 3 completed and succeeded for every source module.
3. No Error was recorded, including suppressed ones.
4. Its outcome is Completed.

Display, suppression, limits and supplementary text never affect acceptance. Every rejected result publishes at least one Error that explains it. If a phase ends incomplete and neither that phase nor an earlier one recorded an Error, the check reports one **fallback** Error at the phase's first incomplete subject: `PrerequisiteUnavailable_Kd` without a known cause for front-end analysis, or `ProjectPreparationFailed_Kd` for input preparation. A fallback marks a missing report, which is a compiler defect to repair; a rejection that stays unexplained violates the diagnostic contract.

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
- If unestablished: the observed failure, that is, its kind and its unabridged failure text. A diagnostic may shorten that text for display; the identity never depends on display. A repeated failure keeps its identity, and a changed cause changes it.

Every comparison uses the whole identity. The same-input comparison compares established inputs only; a read failure is reported first, where the command reads the file. An open document's bytes are its file's BOM (if any) followed by the UTF-8 text, so an unchanged overlay reproduces the disk bytes. Its identity is therefore compared by establishment, BOM and text. Text that cannot be encoded is reported as `InvalidSourceEncoding_Kd`, like invalid UTF-8 on disk.

### 23.3.5. Revisions and recorded inputs

Every input has a globally monotonic **revision**, which changes exactly when a comparison finds a different identity. An input without a retained identity gets a new revision when it is compared. Every result records the inputs it used with their revisions, including the uncompiled path of a same-input comparison. Revisions and comparisons are session-local; durable content identity belongs to the CSP (§23.5).

### 23.3.6. Diagnostics

A check explains each independent problem with one primary record and the evidence it needs. Diagnostics belong to one check request, never to a compilation-wide or named collection, and every output converts the same finalized records.

#### 23.3.6.1. Codes and categories

Each diagnostic code has one severity and one **category**:

| Category | Meaning |
| --- | --- |
| `Language` | The source violates a language rule. |
| `Proof` | A required fact could not be established, including a requirement left undecided by a failed prerequisite. |
| `Unsupported` | The form is valid but outside the implemented subset. |
| `Input` | An input, the configuration, dependency content or a lock could not be established or is invalid. |
| `Resource` | A finite compiler resource limit was reached. |
| `Internal` | A compiler defect. |

- A code accepts no free text. Its message, label and Reason come from its typed facts. A difference within one requirement is a Reason value; a different requirement or category is a different code, except for `Unsupported_Kd` below, and wording alone never adds a code.
- A form this specification permits but the implementation does not support is reported with the single code `Unsupported_Kd` (Error, `Unsupported`), never with a `Language` code, such as a conservative conflict. Its record holds the code and the primary location only: no Reason, Note, Advice, related location or repair candidate. The first phase that cannot handle the form reports it once, and the parts that depend on it produce no records.
- Established facts are stated in the Reason; suggested intent is a repair candidate with its conditions (§23.3.6.9), or Advice, which states its conditions in prose. A `Proof` failure is never described as a false condition, and no edit or guarantee is inferred from Note or Advice prose.

#### 23.3.6.2. Records

A **diagnostic record** holds only the fields that have a basis:

| Field | Content |
| --- | --- |
| Code, severity, category | The code's name, its severity and its category. |
| Message | Explains the problem on its own. |
| Primary location, label | The smallest range that shows the subject of the failed condition, and a short description of that range. |
| Reason | The code's small typed facts. Numbers, enumeration values and Booleans are exact; Types, Constraints and long names are bounded display values with an elision mark. |
| Related | Locations with roles, such as a declaration, an earlier Move or an opening delimiter, each optionally labeled. |
| Note, Advice | Further explanation; conditional repair advice. |
| Repairs | Repair candidates (§23.3.6.9): alternative structured edits that resolve the problem, each with its verified and required conditions. |
| Omissions | The parts that limits summarized or omitted, with counts when known. |
| Display data | Lines and columns, a bounded source excerpt and the alternative text of related locations. Never used for semantic decisions. |

A record whose primary location lies in [excluded syntax](19-compile-time-directives.md#195-diagnostics-and-excluded-syntax) has one related location with the role `excludedBy`, at the innermost excluding directive (the Condition of an `#if`, or the header of a `#case` arm); it is not a Reason fact, so a record keeps its code, primary location and Reason in every Compilation. A record holds no compiler object, analysis state or deferred computation. An essential fact is never placed only in an omissible supplement; differences between instantiations or conditions are explained with the parameter, use or Type argument that distinguishes them, except in an `Unsupported_Kd` record, which names none. The recorded inputs, configuration and reporting unit belong to the result and are not copied into records.

#### 23.3.6.3. Locations and the source table

A **location** is a source reference with an optional span: `start` and `length` in UTF-16 code units of the immutable source text the check read, excluding the end. A zero length is an insertion point, a location without a span is the whole input, and a location without a source is a problem outside every source. A span lies within its source. A record is attributed by its source, never by a collection name.

The **source table** of a result lists every source its records name:

- Each entry has a display path and, for a recorded input, the input and its revision. The same path read as different snapshots gives different entries.
- Entries follow the order in which the check consumed their sources. A source without an input record (built-in, generated or parsed from text) takes its display path from its creator: its module, or its generating Mod and addition order.
- An input that failed to read has an entry without content; records name it without a span.

Lines, columns and excerpts are computed from the same immutable source when the result is finalized; the disk and editor contents are never read again.

#### 23.3.6.4. Problems, prerequisites and suppression

A **problem** is one failed requirement of one subject in one context, with one code. A context, such as an instantiation, belongs to a problem only when the requirement's outcome or facts depend on it. A [Semantics case](08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations) under which a problem was found is a Reason fact of the problem, not a context: one problem found under several cases is one record whose `case` fact names them all, and a problem that holds in every case shows no case.

- Each problem is reported at most once. Repeated reports merge; two problems at the same position are both reported, except that the unsupported forms at one location are one `Unsupported_Kd` problem, never divided by instantiation, Semantics case or requirement. A problem spanning several subjects is normalized before it is reported: for a duplicate declaration, each later declaration is a subject with the first one as a related location.
- A check that cannot decide its requirement because another requirement failed names that **prerequisite** explicitly. It is a **derived** problem, reported as `PrerequisiteUnavailable_Kd` (Error, `Proof`); its Reason names the requirement and the missing condition.
- A derived problem is suppressed only when every prerequisite leads, without an unresolved or cyclic link, to an Error of a directly established problem published in the same result. Otherwise it is published, and it never suppresses another record. A prerequisite without an Error, with only warnings or outside the result is unresolved.
- A prerequisite identifies a check independently of its diagnostic codes and refers to every Error of that check. One directly established Error does not explain another unresolved or cyclic Error at the same check.
- Causes are never inferred from positions, syntax ancestry, earlier diagnostics or text.
- Syntax that failed to parse is a prerequisite of every check that depends on its recovered form.
- Warnings are never derived: a warning check whose prerequisite failed is not reported. The nested parts a failed check skips are explained by its failure and produce no records of their own.

#### 23.3.6.5. Explanation and limits

Explanations are formed once, when the result is finalized, and only for published records. Limits apply per record, never per result:

- Long Types, Constraints and names use one bounded form. A mismatch keeps the differing parts and elides the common ones.
- Related locations and evidence are ordered by role, then location, then value; the first entries up to the limit are kept, with an omission count.
- An excerpt is bounded in lines, characters and display width around the primary location.
- The text of an input failure or exception comes from its code and failure kind; environment-dependent text is a bounded Note.
- A record carries at most a fixed number of repair candidates, each with a fixed number of edits and a bounded total size of edit text. A candidate that exceeds a limit is omitted whole and counted in Omissions as `repair candidates`; no edit is ever truncated.

Limits never change a problem's identity, category, survival, location or the acceptance of its result. Outputs arrange the finalized explanation in a fixed form; they neither add explanation nor truncate it again.

**Origins.** A Reason names an Origin by a kind and a bounded string. Only fixed and finite Origins are displayed, such as the ends of a failed relation chain; an inferred region never is. Where a Reason would name an inferred region, such as the failing Origin of an `Owned` failure, it names the fixed or finite Origin whose relation to that region causes the failure, such as the borrow `local@ref`. In the JSON document (§23.3.6.8), such a fact has the value kind `Origin`, its `value` is the string and its `origin` is the kind.

| Kind | Origin | String |
| --- | --- | --- |
| `expression` | A fixed Origin that an Origin expression (§15.2.1) can write: `static`, a signature name, a parameter or receiver, a projection such as `p.a`, or a meet | The expression written in the signature; without a written name, the parameter or projection |
| `borrow` | A finite Origin (§15.6.5), and in a Closure body the fixed Origin of a capture item's Borrow or Reborrow | The source text of the Borrow (`local@ref`, `State.count@ref`); for an implicit Borrow, that of its Place or temporary (`node`, `makeResource()`); for a capture item, the item (`bias@ref`, or `view` for a bare entry that Reborrows) |
| `omitted` | A fixed Origin without a name or projection, such as the slot of `View` in `items: ref/Array<View<T>>`, or the outer Origin `o` of a pair binder (§8.1.1) | The source text of that Type occurrence, or of the `<s/T>` declaration |
| `closure` | A fixed Origin of a Closure's internal call contract (§7.6.3) that no Origin expression can write: its call receiver, or the Origin of a whole result inferred from the body (§15.8.2) | The fixed text `call receiver` or `call result` |

- A `borrow`, `omitted` or `closure` string is never written as an Origin expression: a Reason names "the borrow `State.count@ref`", never `during State.count` (§15.9), and "the closure's call receiver", never `during self`.
- These three kinds also add a related location with the role `origin`: a `borrow` or `omitted` end at its syntax, and a `closure` end at the header of its anonymous function, from `func` through the parameter list. The string stays in the Reason, so the fact never rests only on an omissible supplement (§23.3.6.2).
- This display serves every Reason that names an Origin, including Origin relation and Origin contract records (§15.6.1), `Owned` failures (§15.2.3) and the records of §15.3.2 and §15.4.3.
- A Type mismatch is a failure of the structural part of a fit (§15.6.1), so its display neither compares nor shows Origin bindings.

The Reason of an Origin relation record (`UnsatisfiedOriginRelation_Kd` or `UnprovenOriginRelation_Kd`, §15.6.1) holds `relation` (`outlives` or `==`), `longer` and `shorter` (Origin displays) and `source` (`fit`, `declared` or `wellFormed`). With `fit` it adds `destination`, the destination Type, in which only the Origin at the failed position is shown; with `declared` the relation clause is a related location with the role `relation`. At a call (§15.6.4), argument fits, invariant equalities included, are `fit` with the parameter Type as instantiated as `destination`, and substituted clauses are `declared` with the callee's clause as that related location; a relation of an instantiated parameter Type's well-formedness is `wellFormed` at its argument, or `declared` with the Type's own clause as that related location, and a `borrow` end that another argument supplies is related at that argument's Borrow. The Reason of an `Owned` failure (§15.2.3) adds `origin`, an Origin display, and `member`, the part through which that Origin enters OwnedOrigins: the outer Origin, the Semantics target, the n-th argument, a base, a Field name, the payload, the n-th component, the element or a capture name. It names the first member that is Refuted in depth-first order over the enumeration of §15.2.3, or else the first Unknown one; Omissions count the others. The Reason of `UnprovenOriginContract_Kd` (§15.6.1) holds `comparison` (`conformance`, `specialization` or `conversion`), `member` (the receiver, the n-th parameter, the result or an `origin` clause of the implementation), `relation`, `longer` and `shorter`. Its ends are rigid symbols of the comparison (§15.3.7), never an instantiable call Origin of the implementation: an Origin of the required contract, displayed as that contract writes it, with a per-call Origin without a written name displayed as `omitted`, or another fixed binding such as `static`.

**Construction and inference.** Reports distinguish Type-name/role/access ambiguity, missing slot evidence, structural or Semantics conflicts, invalid acquisition-shape declarations, unproved call correlations, changed selection or prohibited additional contexts in the fixed-binding constructor check (§10.8.1), ordinary applicability/Constraint failures, and post-selection Origin/Loan/acquisition failures. Resource exhaustion is distinct from all of these. Name the supplying arguments, candidate declarations and unresolved slots where relevant; independent errors remain visible when another check lacks prerequisites. CLI, JSON, LSP and semantic inspection use the same committed construction Type, declaration, acquisition plan and evidence.

An explicit-Type-argument repair may use only established complete bindings, snapshot identity and verified selection conditions under §23.3.6.9. Never insert guessed Types. When explicit arguments would remain ambiguous, or a concrete Type cannot be spelled, give Advice without promising a working edit; a named generic helper may supply an explicit construction Type. Inference itself introduces no new repair kind or condition.

#### 23.3.6.6. Order and equality

The records of a result are ordered by source table order, then primary span, then an order defined by the problem's identity: its subject, code, requirement, condition and context, each compared by source position or by a fixed order. Within one source, a record without a span comes first; records without a source come last. Order never depends on arrival, threads, memory addresses, message text or Type display names. The same facts, definitions and limits give the same records in the same order, and record equality compares every field, including display data and repair candidates.

#### 23.3.6.7. Diagnostic contract

The following violate the diagnostic contract and make the result Faulted (§23.3.3): a catalog anomaly or an unknown code; an invalid location or argument; two reports of one problem with different primary locations or facts; a rejection without a published Error; two distinct problems without a defined order; a repair candidate without edits, with overlapping edits, with an edit outside its source or in a source that is not a recorded input, a candidate on a derived record, a kind or condition outside the catalog of §23.3.6.9, or a relevant condition that appears in neither or both of verified and required; and a failure of collection or finalization.

#### 23.3.6.8. Rendering

Every output converts finalized records; none decides meaning again or binds again.

- **Commands** render a result once it is finalized, in its order: the message and code, the primary location as `path:line:column`, the bounded excerpt with the primary span underlined and labeled, related locations, omissions, Note and Advice, then each repair candidate (§23.3.6.9) as `Repair: <title>`, one line ` = path:line:column: insert '<text>'` (or `replace '<old>' with '<text>'`, or `delete '<old>'`) per edit, and ` = verified: <conditions>; requires: <phrases>`. A later phase, such as emission, renders its own result. The order in which inputs are consumed and later work starts is unchanged. `kimi test` writes diagnostics to standard error, so its standard output stays machine-readable.
- **JSON.** `kimi check <project> --Format json` checks one check unit through the shared check entry (§23.3.2) and writes its output to standard output as one JSON document; everything else goes to standard error, and the exit code is unchanged. `--Format text`, the default, is the rendering above. The document is:

```text
CheckOutput := {
  schema:       "kimi.check/1"
  compiler:     the compiler build identity
  unit:         { project, target, mode: "Product" | "Test", debug }
  outcome:      "Completed" | "Blocked" | "Faulted"
  accepted:     bool
  testPresence: "Yes" | "No" | "Unknown"
  sources:      [ { path, isInput, sha256? } ]   // sha256: the recorded input's bytes, computed when they are read for this output
  diagnostics:  [ every field of §23.3.6.2, including repairs ]
}
```

  Field names are camelCase, enumerations are strings, spans are `{ start, length }` in UTF-16 code units, and ranges are zero-based lines and characters. The schema of version 1 is [`docs/spec/schemas/check-output.schema.json`](schemas/check-output.schema.json); a change of the document's shape is a new version. The entry is shared, so the document holds the records the language server publishes for the same inputs.
- **Language server:** §23.4.7 and §23.4.8. The server renders nothing, and its standard output carries only protocol frames.

#### 23.3.6.9. Repair candidates

A **repair candidate** is a structured edit of a recorded input that resolves a record's problem, together with the conditions its acceptance rests on. A record carries zero or more candidates. They are alternatives, ordered by kind in catalog order and then by the position of their first edit; the order is for display and states no preference. A tool never applies a candidate without a request, and the result of applying one is a new input whose validity only a new check establishes; nothing is inferred from the candidate about that input.

| Element | Content |
| --- | --- |
| kind | A stable name from the closed catalog below. It fixes the title template and the names and kinds of the candidate's facts |
| title | One sentence formed from the kind's template and the facts |
| facts | Typed facts, as in a Reason |
| edits | One or more edits, each naming a source table entry that is a recorded input, a span of its immutable text in UTF-16 code units (an empty span is an insertion point) and the replacement text; lines and characters, and the bounded text the span covers, are display data |
| verified | The relevant conditions the check established from its own facts |
| required | The relevant conditions the check could not decide, each with a phrase formed from the condition's template and the facts |

**Conditions.** The vocabulary is closed; adding a condition is a specification change.

| Condition | Meaning |
| --- | --- |
| `Take` | The Place offers Take and is a Movable Place (§15.1.5) |
| `ExclusiveAccess` | The lending point is exclusively writable (§15.1.5) |
| `UsageLegality` | The selected operation and every later use of the affected Place satisfy the usage conditions of §10.6: initialization and Move state, Loans and lifetimes |
| `Structure` | The visibility of existing Names, the order of destruction and `defer`, the evaluation Context and result supply of each expression and the targets of existing control transfers are unchanged |
| `Selection` | The qualified Name selects what its use needs: one Type by Type name selection (§9.6), a Field or Property used as a value, or an applicable function by overload resolution (§10.1–§10.4) |

The conditions relevant to a candidate are fixed by its diagnostic and kind (the catalog below) and, where the catalog says so, by whether the repaired use is a value or a Type. The check judges each relevant condition from its own facts as **verified**, **required** (undecidable without analyzing the edited input) or **refuted**, and a candidate with a refuted condition is not offered. No check analyzes the edited input, so `UsageLegality`, wherever it is relevant, is always required in this revision.

**Edits.** An edit replaces or inserts syntax and supplies the separators its boundary needs: inserted or replaced text is a complete token or item and never merges with an adjacent identifier (`a&&b` becomes `a and b`). A deletion at a line start removes only indentation that exists, and no edit touches an empty line, a line break or the inside of a multi-line literal. The edits of one candidate do not overlap, are ordered by position, and two insertions at one point are one edit; they are applied together, and their text is never shortened or elided. Edits reach recorded inputs only, never `compiler://` or generated sources.

**Offering.** A candidate is offered only when its effect follows from the check's facts; a repair that needs a choice the facts do not settle is Advice. A derived record (`PrerequisiteUnavailable_Kd`) carries no candidates. A record with candidates does not repeat their edits in its Advice: the Advice states the conditions a candidate cannot express and the alternatives, including a repair that was not offered because a condition was refuted. Every output shows the same candidates.

**Catalog.** The kinds, the diagnostics that offer them and the relevant conditions:

| Diagnostic | Kind and edits | Judgment | Offered when |
| --- | --- | --- | --- |
| `TransferRequired_Kd` (§3.5) | `Repair.Transfer`: `@move` after the Place | `Take`: verified or required from the acquisition plan, refuted for a Place without Take (a borrowed, published, static or dynamically indexed Place); `UsageLegality`: required | `Take` is not refuted |
| The same, at a capture entry (§7.6.2) | `Repair.Transfer`: `x@move`; `Repair.Borrow`: `x@ref` | Transfer as above; the borrow has `UsageLegality` required | Transfer as above; the borrow always |
| `ExclusiveBorrowRequired_Kd` (§7.3, §15.1.5) | `Repair.BorrowExclusively`: `@uniq`, or `@objuniq` for an object handle | `ExclusiveAccess`: verified or required from the acquisition plan, refuted for a `let` root, an owned-Type parameter or a getter result; `UsageLegality`: required | `ExclusiveAccess` is not refuted |
| `UnnecessaryUnsafeBlock_Kd` (§14.3.3) | `Repair.RemoveUnsafe`: delete `unsafe => `, or delete the `unsafe` line and one indentation level of each line of its Body | `Structure`: verified when the Body declares no Name and registers no `defer`, refuted otherwise | The statement is a direct item of an indented body, its `unsafe` line holds no other token or comment, and its Body contains no multi-line literal |
| `MisplacedSyntax_Kd` for `&&` and `\|\|` (§2.4) | `Repair.ReplaceToken`: `and`, `or` | None | Always |
| `BorrowOriginKeyword_Kd` (§3.3.6) | `Repair.ReplaceToken`: `during` | None | Always |
| `MissingSyntax_Kd` for a closing delimiter | `Repair.InsertToken`: the closer at the insertion point | None | Always |
| `DiscardedResult_Kd`, the try-success warning (§17.4.3) | `Repair.PropagateFailure`: `_ = try ` before a discarded Result; `Repair.ExplicitDiscard`: `_ = ` before either expression | None: `_ =` makes the right side a Value Context (§14.2.4), so `Structure` is not relevant | The expression is a direct item of an indented body; propagation only when the enclosing function's failure return target fits (§17.2.4); propagation precedes explicit discard |
| `QualificationRequired_Kd` (§9.4) | `Repair.Qualify`: a qualifier before the Name: `self.`, `Self.` or `C.` for the searched Container `C`, or `::P.` for a declaration of a later stage | `Selection`: verified for a Field or Property that is not called, verified or refuted by Type name selection for a Type, required otherwise; `UsageLegality`: required for a value, not relevant for a Type | `self.`: the found declarations include an instance member, and a `self` visible in the same Function Boundary has Effective Core `C`; a construction receiver qualifies only in the constructor body and for an own stored Property (§6.2.3.4). `Self.`: they include a non-instance declaration and `Self` denotes `C`; otherwise `C.` when `C` has no generic parameters or Origin header and the Qualifier `C` selects `C` at the use. `::P.`: the diagnostic exploration of the later stages finds eligible non-instance declarations in a Container reached from the Compilation root by a path `P` without Type or Origin arguments whose every segment is accessible at the use (`::` alone at the project root) |
| `TaskArgumentMismatch_Kd` with the Reason `missing` (§24.5) | `Repair.PassTask`: `task;` right after the `(` of the argument list, followed by one space when an argument follows on the same line (`bar(task;)`, `foo(task; x)`); for an unresolved leading `task` argument, `task;` replaces `task` and its comma | `UsageLegality`: required, because the repaired call is a task call that §24.3 compares; `Selection` is not relevant, because the task argument binds no parameter | The innermost function that contains the call, anonymous functions included, has a task slot, and the call is not directly in a default expression or in the body of a Deferred Block that the function registers (§24.2.2) |

A Receiver Expression that cannot be acquired (§7.3), a colon at a transfer operand (§14.5.1), the Origin repairs of §15.3.2, §15.4.3 and §15.6.1 and `DiscardedValue_Kd` (§17.4.2) need a choice or a Loan verification, so they carry no candidates; their Advice describes the repair.

Callable effect-bound declarations, selected-binding failures (UnsatisfiedEffectBound_Kd, Language, Ownership phase), input and static Loan conflicts, and the typed hover showing all contributing premises follow §8.4.10.7. Effects never choose an overload; proof failure and selected-use effect failure remain distinct records.

## 23.4. Language Server Protocol

### 23.4.1. Transport and lifecycle

- **Framing:** `Content-Length` headers and UTF-8 JSON bodies, with bounded headers and payloads.
- **JSON-RPC:** requests keep their IDs and are distinguished from notifications. A success response carries `result`, including `null`; an error response carries `error` and no `result`. An unknown request receives `-32601`; an unknown notification is ignored. A request before `initialize` receives `-32002`, a request after `shutdown` receives `-32600`, and a body that is not JSON receives `-32700` with a `null` ID; a malformed header ends the input.
- **Capabilities:** only implemented capabilities are advertised: incremental `textDocumentSync` with open and close notifications, the UTF-16 position encoding and, when the client declares `textDocument.codeAction.codeActionLiteralSupport` and `workspace.workspaceEdit.documentChanges`, a `codeActionProvider` for the kind `quickfix` (§23.4.8).
- **Lifecycle:** `shutdown` stops publication, answers and retires the pending check; `exit` ends the process with code 0 after `shutdown` and 1 otherwise, without waiting for a running check. The command host owns the exit.

### 23.4.2. Documents and synchronization

- A document's role comes from its extension: a **source document** (`.kimi`), a **project document** (`.kimiproj`), or ignored (anything else, including lock files). Documents with non-`file:` URIs are ignored.
- A notification's changes apply in order, each to the result of the previous one, whatever their version; a non-increasing version is only logged. A character beyond its line end is clamped, as LSP specifies. Positions are UTF-16 code units, and line breaks follow the source rule (CR, LF and CRLF).
- A change to a document that is not open is ignored until `didOpen`.
- An inapplicable change (a reversed range, or a line beyond the document) makes the document **desynchronized**: it is then an unestablished input, so every check that reads it is Blocked with `DocumentDesynchronized_Kd`. A full-text event (`didOpen`, or a change without a range) resynchronizes it. Both transitions change its identity, even when the text is unchanged.
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

- **Quiet period.** At most one workspace check is pending, eligible at `lastEvent + quietPeriod` (1000 ms by default). Every input event resets the deadline, and there is no maximum wait. `initialized` also schedules a check when projects are selected.
- **Base.** A workspace check is fixed to its **base**, the latest input event number when it starts. A unit is **pending** while one of its known inputs, or an input it read in the check, has an event after the base; its known inputs are its members and the inputs recorded by its prepared unit or previous result.
- **Base rule.** Work that needs an input with an event after the base takes no effect: its comparison is not committed, discovery keeps that project's previous state, a pending unit is skipped, and a result that recorded such an input is discarded at adoption. The state owner decides this; the worker may skip early from a published set of changed inputs, and a stale view of that set only wastes work.
- **Start.** (1) The state owner fixes the base, takes an immutable copy of every changed open document and routes later input to the next check. (2) The worker re-validates, and the state owner commits the comparisons. (3) The worker runs discovery, derives the required units and returns a Blocked result for each required unit without a prepared unit, unless its last result is still valid.
- **Visiting.** Projects with the most recent member event come first, then by project identity, with product units before the test unit. A unit whose result was valid when the comparisons were committed reuses it; a pending unit is skipped, and its test unit with it; every other required unit is checked. A started check runs to completion; the server never cancels it.
- **Adoption.** The state owner adopts a result only if none of its recorded inputs has an event after the base and its unit is in the adopted required set; otherwise it discards the result. A test result is kept only if the adopted product results require the test unit.
- **Retention.** Content and derived data are kept only while an open document, a current discovery, a required unit or the running check needs them; a project that discovery no longer reaches is released with the inputs only it retained.

Example: with the default quiet period, edits at 0, 90 and 180 ms start one workspace check no earlier than 1180 ms. An edit at 1210 ms comes after that check's base. The unit running at 1210 ms completes, and its result is discarded if it read the edited source; later units that need the source are skipped; URIs whose contributors all remain valid are published as each unit finishes. The next check starts at 2210 ms or when the worker becomes free, whichever is later, and rechecks only the invalid and skipped units, starting with the edited project.

### 23.4.7. Publication

- **Report URIs.** A unit's report URIs are the `file:` sources it checked plus the display URIs of its diagnostics. A URI's **contributors** are the required units whose latest results report to it.
- **Ranges.** A record's span is sent as a range only when its source table entry is a recorded input of that result. This is decided once per result and entry and never adjusted to the current editor contents; any other record is shown without a range.
- **Display placement.** A record without a range is shown at the start of its file. A record whose source is missing, `compiler://` or generated without an input record is shown at the start of the unit's project file, or of the implicit source, and its text names the original location. The record keeps its original location.
- **Updates.** A URI is reconsidered when a contribution to it changes (a result is adopted or a unit retires) or when a contributor is released from a hold, and at no other time.
- **Condition.** A URI is sent only when all its contributors have valid results. A pending unit keeps its last result, so its URIs keep the last sent diagnostics until it runs again. A Blocked result is not pending, so a failure replaces earlier diagnostics. A required unit without a result is not yet a contributor; its diagnostics can only extend a current set.
- **Diagnostic.** Each diagnostic has a range, severity, code, `source: "kimigayo"` and message. The message is the record's message followed, on separate lines, by its label, the alternative text of related locations not sent as `relatedInformation`, omissions, and `note:` and `advice:` text. Related locations with a sendable range are sent as `relatedInformation` when the client declares `textDocument.publishDiagnostics.relatedInformation` at `initialize`; otherwise, and for every other related location, their alternative text is placed in the message.
- **Payload.** `textDocument/publishDiagnostics` carries a complete replacement: the contributors' diagnostics for the URI, ordered by display range, then contributor (project identity, unit kind, target), then result order. Diagnostics with equal sent values from different contributors merge, keeping the largest count that one contributor sends; distinct problems of one result never merge. The notification carries the document version when the document is open. Version handling is optional for clients, so correctness never depends on it.
- **Suppression.** A URI never sent counts as sent empty. A non-empty payload equal to the last send, with the same version, is not sent again, and an empty payload is sent only after a non-empty one.
- Changes of an outcome are logged with `window/logMessage`, never shown with `window/showMessage`.

### 23.4.8. Code actions

- **Capability.** `codeActionProvider: { "codeActionKinds": ["quickfix"] }` is advertised only when the client declared both `textDocument.codeAction.codeActionLiteralSupport` and `workspace.workspaceEdit.documentChanges`; `codeAction/resolve` and commands are not used.
- **Matching.** `textDocument/codeAction` is answered from the diagnostics last sent for the URI, without running a check and without reading `context.diagnostics`. A sent diagnostic matches the request when both ranges are non-empty and the half-open ranges intersect, when one range is empty and its point lies within the other with the end included, or when both are empty and equal.
- **Validity.** Candidates are returned only while the result that sent them is valid (§23.4.5). A document change marks its input, so after an edit, a change with the same version, a close and reopen, a desynchronization or a change of a dependency alone, the request returns no candidate until the next adoption restores them.
- **Response.** For each matched diagnostic and each of its repair candidates (§23.3.6.9) whose edits all lie in the requested document, one `CodeAction`: `kind` is `quickfix`, `title` is the candidate's title followed by `; requires <phrases>` when it has required conditions, `diagnostics` holds the matched sent diagnostic, and `edit.documentChanges` holds one `TextDocumentEdit` with the document's current version. When `context.only` is given and does not include `quickfix`, the result is empty. A candidate whose edits reach another document is not returned in this revision.
- **Contributors.** When several units report one URI (§23.4.7), a candidate is returned only when every contributor sends it with equal values, with its source mapped to the URI. The candidates of a URI are updated whenever a result is adopted, also when the payload is unchanged and not resent.

### 23.4.9. Watching and session settings

- **Watching.** At `initialized`, when the client supports dynamic registration, the server registers `workspace/didChangeWatchedFiles` once for `**/*.kimi`, `**/*.kimiproj` and `**/*.kimi.lock.json`. Without watching, and for inputs outside watched folders, disk changes are found by re-validation at the next check that a document event starts; a change that keeps both timestamp and length is then found only when the file is opened or the session restarts.
- **Read-only.** A check never restores packages, rewrites locks or edits project files.
- **Settings.** `initializationOptions` is validated once and fixed for the session. Unknown members are ignored with a log message; an invalid value is rejected with a log message and replaced by its default.

| Member | Default | Meaning |
| --- | --- | --- |
| `checkQuietPeriodMs` | `1000` | Quiet period, 0–10000. |
| `selectedProjects` | `[]` | Absolute paths or `file:` URIs of `.kimiproj` files to treat as roots. |
| `target` | none | The target of product units (§23.4.4). |
| `allTargets` | `false` | Checks every configured target. |
| `debug` | `false` | The `Debug` setting of every unit. |

### 23.4.10. Example session

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

### 23.4.11. Hover

[Hover](lsp-hover.md) defines declaration containers and source attribution, Type/Copy, Property, function, local/parameter Type composition, built-in `@` operation explanations, effects and documentation inspection, including source-relative links. It shares this section's checks, quiet-period setting, input validity and required units, while keeping its own complete participant agreement and explicitly labeled previous-analysis display. Diagnostic publication does not depend on Hover readiness or rendering. Associated-Type omission diagnostics and inferred-binding Hover follow the shared [cause, location and provenance rules](associated-type-inference.md#6-publication-diagnostics-and-reuse).

## 23.5. Compiler Server Protocol

**Design status: specified, not implemented** (Appendix D).

### 23.5.1. Purpose

The CSP will give programs, including AI agents, a structured interface to the compiler:

- **Diagnostics:** causes, related locations and repair candidates with explicit preconditions and guarantees.
- **Syntax inspection and manipulation:** inspect syntax and semantics, apply edits against identified source snapshots, and produce reviewable source changes.
- **Verifiable changes:** associate checks, tests and measurements with exact source and configuration, and report their outcomes and remaining uncertainty.

### 23.5.2. Provided foundation

The check foundation (§23.3) is the CSP's base: the check entry, check units with their effective configuration, outcomes, diagnostic records with their source tables and recorded inputs, their repair candidates (§23.3.6.9) and the JSON document of `kimi check --Format json` (§23.3.6.8). The CSP adapter uses them exactly as the language server does. Revisions and comparisons are session-local.

### 23.5.3. Requirements

Arithmetic inspection retains the selected operator/Contract, provider, mapping, Output, acquisition and writeback facts defined in [arithmetic Contracts §6](arithmetic-contracts.md#6-diagnostics-and-retained-meaning). Adapters use the source-associated facts rather than resolving again for presentation.

When the CSP is introduced, it must:

- identify every source snapshot by durable content identity, so that edits, results and evidence name exactly the inputs they rest on;
- apply edits only against an identified snapshot and return them as reviewable source changes, never as silent file writes;
- apply a repair candidate (§23.3.6.9) only against the snapshot that identified it, and bind the check of the edited input to it as a verifiable change, establishing there the conditions that need analysis of the edited input, such as `UsageLegality`;
- report the diagnostic records of §23.3.6 unchanged, in the JSON form of §23.3.6.8;
- give stable handles to syntax and semantic nodes within a snapshot; syntax handles include excluded syntax (§19.5) and state that the node is excluded and which innermost directive excludes it, while semantic handles cover selected syntax only;
- bind every check, test and measurement to its exact source and configuration, and report its outcome and any remaining uncertainty;
- list the places where unsafe promises are made, each with the obligations to satisfy (the conditions of §5 and the `- safety:` item): every Unsafe Block with the operations that use its permission, every call of an unsafe function, and every `#LibraryImport` declaration;
- in that listing, report each Loan anchored by a raw-Place borrow (§5.2.2) that is live across a suspension point as an obligation fact of the Unsafe Block that formed it;
- list each suspension point (§24.1) with these facts, local to its signature and body:
  - the task slot that it passes;
  - the Loans and owned values live across it;
  - whether it may report cancellation: the selected callee's instantiated result Type reaches `Kimi.Async.Cancelled`, identified by its declaration identity, through enum payloads, Tuple elements and the arguments of `Option` and `Result`. This is a may-fact, unknown for an abstract Type argument and never derived from bodies (§18.7.2);
  - for a call that starts children, the Loans that the children capture;
  - for an `Async.shield` call, its grace;
- report whether an instance is plain (§21.6.1) per monomorphized instance, apart from the local facts of suspension points;
- report a task frame's size only as a measurement bound to the O level and toolchain identity (§21.6.6), never as a check fact;
- keep the principle of §23.2 and the outcomes of §23.3.3, and never narrow a language rule.
