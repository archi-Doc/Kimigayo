# Kimigayo Diagnostics

Developer notes for the compiler's diagnostics. SPEC [§23.3.3](../spec/23-compiler-services.md#2333-outcomes-and-acceptance), [§23.3.6](../spec/23-compiler-services.md#2336-diagnostics) and [§23.4.7](../spec/23-compiler-services.md#2347-publication) define the observable behavior; this document records the internal model that realizes it, the rules that keep it consistent, the migration rules of the Diagnostics track (PLAN.md) and the detail of the [Diagnostic development workflow](../../AGENTS.md#diagnostic-development-workflow). It never overrides the specification.

## 1. Principles

| ID | Principle |
| --- | --- |
| I1 Definition | The meaning and definition of a diagnostic are decided in one place. |
| I2 Determinism | Published content and order never depend on arrival, threads or wording. |
| I3 Explanation | Suppressing a cascade never removes the record that explains the problem. |
| I4 Acceptance | Acceptance never depends on display, suppression or supplements. |
| I5 Locality | Adding an unrelated problem never reduces the explanation of another. |
| I6 Boundary | Published content is finalized once and never changed afterwards. |

## 2. Common rules

Each rule replaces a family of special cases. A change that needs an exception to one of them first revisits the rule.

1. **One requirement, one phase.** A requirement is judged by the phase of its owning partition. A later phase that depends on it names it as a prerequisite and never reports it again.
2. **No free text.** A code's message, label and Reason come from its typed facts. A difference within one requirement is a Reason value; a different requirement or category is a different code; wording alone never adds a code.
3. **Unsupported is its own category.** A form the SPEC permits but the implementation does not support uses an `Unsupported` code, with the missing feature in the Reason.
4. **One node state, many facts.** A syntax node keeps one analysis state, which its first failure sets. Diagnostic facts are kept per problem key and recorded at the final decision; a second failure adds a fact without changing the state.
5. **Context only when it matters.** A context (instantiation, expansion) is part of a key only when the requirement's outcome or Args depend on it; a check without a context has no instantiation-specific Args.
6. **Total order by structure.** Every key component has a total order: source table order and span for positions, a fixed order for enumerations, and a structural order for Types (declaration source and span, then Type arguments recursively; built-in Types in a fixed order). Display names are never compared.
7. **Records are independent of display.** A record is finalized without knowing its output. Only limits are settings: constants in one place, replaced only by tests.
8. **One fallback rule.** A phase that ends incomplete, with no Error in its partition or an earlier one, reports one fallback at its first incomplete subject (SPEC §23.3.3). The contract check for unexplained rejections is the last safety net. Tests treat any fallback as a defect.
9. **One fault record.** A Faulted result is explained by exactly one `CheckFaulted_Kd` whose Reason carries the fault kind. `InternalInvariant_Kd` is reserved for internal failures in a Completed result.
10. **Decisions look backwards.** A decision queries only the partitions that precede it: library validation reads the library's syntax partition, emission and acceptance read every front-end partition.
11. **Introduce at first use; rewrite each call site once.** A concept arrives in the stage that first uses it, and a reporting call site is rewritten once, with its generator, key, prerequisite and location together.

## 3. Definitions

| Source | Holds |
| --- | --- |
| `src/Kimi/Diagnostics/DiagnosticCode.tinyhand` and `DiagnosticEntry` | Severity, category, and default Message, Label, Note and Advice |
| Phase-specific reporting conversions (C#), checked against the catalog schemas | Argument and evidence values, roles of related locations, conditional explanations |
| `DiagnosticRequirement` (C# enumeration) | The requirement's stable name (public vocabulary, like code names), its owning partition and its semantic order |
| `DiagnosticRequirement.tinyhand` | A short description per stable name |

- Enumerations are written by hand; a consistency test keeps enumeration, catalog and description table equal. No enumeration is generated.
- The catalog is validated when it loads: unknown, duplicate and missing entries, invalid severities/categories, malformed or duplicate fact names and a template whose placeholders differ from its schema's arity are catalog anomalies. Message templates are parsed once, at load. Record-time checks reject nonnumeric Number values, undefined Enumeration values and non-Boolean Boolean values. The reporting APIs currently accept objects; this validation is not a claim that every recorder has a separate statically typed generator.
- `PrerequisiteUnavailable_Kd` (Error, `Proof`): "The prerequisite of this requirement could not be established, so the requirement cannot be decided." Its Reason is `requirement` (a stable name) and `condition` (the condition and the fact it needs).
- The former `Fix` field is Advice, with no alias. The former `hint` argument is gone; its content is a Note or Advice built by a typed generator.
- Callers obey the definition: a serialized source in an incompatible format is an `Input` problem with its own code, never `UnexpectedToken_Kd` with prose.

## 4. Model

### 4.1. Facts and keys

A logical model; it requires no allocation per field.

```text
DiagnosticFact
  Code        : DiagnosticCode
  Subject     : a syntax node, token, input or compiler process of this analysis
  Check       : requirement and condition number (independent of the code)
  Context     : instantiation or expansion context, or none (rule 5)
  Primary     : primary location
  Args        : the code's typed facts
  Evidence    : supplementary evidence with roles
  DerivedFrom : check keys of the unmet prerequisites (an unresolved mark when unknown)
```

- The **check key** is `(Subject, Check, Context)`; the **problem key** is `(check key, Code)`. Identity never uses display ranges or strings.
- A requirement is distinct across phases: establishing a declaration's Type and a call's argument count are different requirements. The condition number distinguishes arguments, Contract conditions or Places within one requirement.
- A Subject distinguishes source snapshots and analysis generations. A syntax error names a token or boundary; an input failure names the input. A synthesized node is identified by its original subject, its transformation role and its order within the transformation.
- A problem with several subjects is normalized before it is recorded (SPEC §23.3.6.4).
- Values that can change after recording (a rebound call, reused scratch storage) are captured as values; immutable analysis data may be referenced. Prerequisite, evidence and related-location arrays are copied at the recording boundary, including the first report of a problem.
- Analysis-specific failure enumerations remain; each converts to diagnostics in one place (Binding's `Check` switch, `OwnershipIssue.Code`, the control-flow conversion).

### 4.2. Owner, partitions and error state

- One **diagnostic owner** per check request is created before input preparation and passed to the compilation and later phases. It replaces named collections, `IsGlobal`, the mutable current `SourceDocument` and positional removal.
- Facts are recorded in **partitions** that follow the existing invalidation boundaries:

| Partition | Invalidated when |
| --- | --- |
| Input and configuration | Kept for the whole check request |
| Syntax (one per module) | That module's syntax tree is rebuilt; it also holds the recovery map |
| Semantics (Binding, startup, control flow and ownership) | Any syntax changes (all of it), or one phase is analyzed again (that phase and later ones) |
| Later phases | One per result unit |

- Invalidation replaces a partition's fact list as a whole and never scans facts; `BeginSourceParsing` must stay cheap. Production analyzes each compilation once; repeated analysis happens in tests and benchmarks.
- Recording an Error sets the error flag of its partition and module before aggregation or suppression; a valid partition never clears it.
- Acceptance, emission and library validation query the owner under rule 10. A decision inside one call (token caching, `Documentation.Finish`) uses that call's local result. `errorCount`, `ErrorVersion` differences and `HasSourceErrors` are removed.
- A tentative retry inside an analysis stays inside it; only the adopted decision becomes a fact.

### 4.3. Recording, aggregation and prerequisites

- Facts aggregate by problem key when recorded. For one key, Primary and Args must be equal (a contract violation otherwise); causes and captured evidence are unions, and evidence keeps the first N by the order of SPEC §23.3.6.5.
- Invariant: `Code == PrerequisiteUnavailable_Kd` exactly when `DerivedFrom` is not empty. An unknown cause records the unresolved mark.
- Only Errors are derived. A warning check whose prerequisite failed records nothing.
- The recorder of a derived failure names the unmet prerequisites by check key; the prerequisite's code may be unknown and recording order does not matter. A prerequisite key stands for an Error that prevents the referenced requirement; another requirement gets another key.
- Nested parts skipped because a check failed are explained by the failure and get no facts. When an explicitly referenced prerequisite was itself skipped, the derived fact inherits the skip's cause.
- **Consultation (Binding).** Each checked node is bound in a frame: expressions (`BindNode`), Type expressions (`BindType`), variables (`BindVariable`) and a call's member callee. A frame records the operands the node read that did not resolve, and every node failed while it is open. A use of a parameter or property whose Type failed consults the written Type. An expression bound against an expected Type whose written syntax failed (an initializer, an assigned value, a returned value) names that syntax where it needs the expectation, and a call with no applicable candidate rests on a candidate whose signature failed. An operand that resolves later is not a prerequisite, and a node whose lookup succeeded but was never completed contributes nothing. A failure that reports missing information (`MissingName`, `MissingType`, `Unsupported`) with an unresolved prerequisite is derived; every other failure is direct.
- **Recovery.** When parsing fails, recovery links what it keeps to the key of the lexical or syntax check that caused it: an `ErrorKoto` carries the key (`TokenReader.NewErrorKoto`), and synthesized syntax kept in place of a rejected form, such as the combined node of a chained comparison, is recorded in its `CodeContext` (a map that exists only after a recovery). Binding checks the operands of a recovered form on their own, not its guessed combination, and names the key as the prerequisite of every check that depends on the recovered node.
- **Later phases.** A startup or control-flow check that reads Binding's result at a node that Binding failed, or left resting on a failure, is derived from those Binding causes (`Binding.FailureCauses`); an incompatible result also rests on its consumer, the node that reads it. This covers a later phase repeating Binding's own check of one requirement. Checks judged from syntax alone (jump targets, labels, fallthrough, Unsafe Blocks, `while true`) stay direct, so independent problems at one node survive. A warning whose check rests on a Binding failure records nothing.
- **Suppression.** At finalization, valid facts are indexed by check key and each prerequisite is bound to every Error its check reported. A prerequisite without an Error, with warnings only, outside the result or in an invalid partition is unresolved. A derived fact is suppressed only when every cause reaches a published direct Error without an unresolved link or a cycle; otherwise it is published and suppresses nothing. Causes are resolved in one non-recursive pass that shares the verdict per prerequisite key; suppressed facts are released without being published.

## 5. Explanation, limits and order

- Explanations are formed once at finalization for published records (SPEC §23.3.6.5). Limits are constants in one place (`DiagnosticLimits`: excerpt lines and width, displayed value length, Note length, related locations); tests may replace them.
- **Facts.** The catalog names every message argument (`Arguments="name:Kind"`, with Kind `Number`, `Enumeration`, `Boolean`, `Text` or `Type`) and the evidence a recorder may add (`Evidence`, all of it or none). The Reason is the arguments, then the evidence. A `Label` may reference them in that order and is omitted when a report lacks a referenced fact. A recorder may also name a primary location other than its subject (`at`) and related locations with roles.
- One bounded formatter (`DiagnosticText`) serves Types, Constraints and long names; the two Types of a mismatch keep their differing parts and elide the common head and tail. Environment text is a bounded Note. The message and label display the bounded values.
- Related evidence is unioned by its full captured value, then ordered by ordinal role name, consumed source order, span and full value, before display bounding or limiting. Locations without a source come last within a role. The final source table contains only published primary/related sources, in consumption order; related-location arrival cannot change source order. What a limit drops is counted in the record's Omissions (related locations, excerpt lines).
- Order and equality follow SPEC §23.3.6.6 and rule 6. The semantic order compares subject, code name, requirement, condition and context, using the same information as identity.

## 6. Diagnostic contract and faults

- Violations (SPEC §23.3.6.7) go through one fault path. It discards the partial records and reports `CheckFaulted_Kd` with fixed text and the fault kind as its Reason. The fault path depends on neither the catalog nor ordinary formatting; its fixed values live in one place, and a test checks them against the catalog.
- When an analysis throws while the diagnostic path is intact, the valid facts are kept, `CheckFaulted_Kd` (kind `Exception`) is added, and the result is Faulted. `CheckService` no longer formats its own records.
- Cancellation and waiting for an unestablished input keep their current handling.

## 7. Outputs

- **Commands** print a result once it is finalized; `Project.BuildTarget` has one finalization point for every return path. `kimi test` keeps diagnostics on standard error.
- **Language server.** `LspDiagnostic` equality is structural, including related information, because deduplication and repeat suppression compare values. The client's `relatedInformation` capability is read at `initialize`.
- **JSON** is prepared but not emitted: a source-generated serializer writes records, and the snapshot harness (§9.1) uses it for evidence. It is not a public schema.

## 8. Migration rules (Diagnostics track)

The stages and their completion conditions are in [PLAN.md](PLAN.md). These rules hold while the track runs.

- **D1** changes no published diagnostic. Tests read diagnostics through one helper, so later stages change only that helper.
- **D2a** keeps the old start-offset suppression as a transitional filter: per old collection unit (the `CodeContext` collection name), by start offset, with a rangeless location counted as 0, keeping the first fact in recording order (partitions in phase order, then arrival). It is not improved. Contract checks that depend on problem keys (conflicting facts, unexplained rejection, undefined order) stay off; the others are on.
- **D2b** migrates recorders upstream first: lexing and parsing (with the recovery map), Binding, startup, control flow, ownership. The transitional filter drops only facts of recorders not yet migrated; facts of migrated recorders still occupy their offsets. Each recorder is rewritten once (rule 11). When the last recorder is migrated, the filter, `DiagnosticDependencyVisitor` and the `BorrowOriginHint` suppression are removed and every contract check is on. Done: every recorder reports problem identities, the filter and the former-collection units are removed, and a rejected result without a published Error is the `UnexplainedRejection` fault in both the check entry and command rendering. `BorrowOriginHint` now only supplies a Note and classifies a failure it explains as direct.
- A path never runs the old and new methods together, and no intermediate identity such as `(range, code)` is introduced.
- Explanations are bounded from the start.

## 9. Evaluation

### 9.1. Snapshot harness

One harness checks a fixed corpus through the check entry: milestone programs, rejection fixtures, examples and mutation cases. It writes normalized records, outcomes and acceptance as JSON under `artifacts/verify/`, and classifies differences against a baseline by kind: case, acceptance, code, severity, attribution, location, text and order. Timing belongs to the separate measurements (§9.4). A stage lists the kinds it intends; any other difference needs review.

### 9.2. Mutation tests

- Mutate a milestone program that passes, or a part verified independently. Change a copy in memory or under `temp/`, never the original, and confirm that the edited text is unique. An unfinished program with existing errors is never a single-error base.
- A case records its base source, effective configuration, change, intended problem and expected public records. Expectations are written independently of the generators.
- Cases cover definitions and recorders, prerequisites and cascades (including recovery), error state and invalidation, outcomes and faults, locations and snapshots, outputs and limits.
- Compare codes, requirements, Reasons and locations. Full-text comparison is limited to a few display tests, and people review underlines against explanations. A lower diagnostic count is not a goal.

### 9.3. Input relations

- Adding a blank line outside strings and documentation comments changes positions only.
- For the same check, input and prerequisites, adding an unrelated problem leaves existing primary records and explanations unchanged.
- Changing wording alone leaves identity, suppression and order unchanged, and the change of the published values is detected.
- Reordering inputs whose order the SPEC leaves free leaves records and order unchanged. The compiler has no hook for reordering its own work.
- Changing limits never changes a problem's survival, category or acceptance.

### 9.4. Performance

- Paths without diagnostics gain no per-node allocation or string formatting; only published records are formatted.
- Targets: resolving causes in O(F + E) for F facts and E prerequisite references, ordering in O(D log D) for D published records, explanations in O(D × K) for a per-record limit K.
- Compare time, allocations and retained size before and after on the same machine, configuration and input: the existing warm zero-allocation probes, front-end timings of milestone programs, and the LSP measurements. A reproducible regression is fixed; an unmeasured improvement is not claimed.
- Analysis is single-threaded and each compilation owns its diagnostics, so recording needs no lock.

### 9.5. Evaluation (D4, 2026-09-30)

- **Snapshot.** 80 cases through the check entry (40 milestone programs, 28 examples, 12 mutations): 60 accepted, 20 rejected, every outcome Completed, 85 published records, no fallback, no unexplained derived fact and no fault. Each stage's differences were reviewed against its intended kinds: D2a none; D2b none for Binding, 49 code and location differences for implementation limits, 2 for the control-flow codes and 5 new cases; D3 11 location and text differences (`artifacts/verify/20260930-064053-358-unit-d3-explanations`).
- **Mutations.** 12 cases covering assignment, access, overloads, a generic transfer, a capture, a borrow Type, a missing operand, a chained comparison, an unclosed call, a misspelled Type and two independent errors. The published Errors of every phase are exactly the expected codes. `DiagnosticPrecisionTest` also covers Unsupported limits, derived expectations, cross-phase repeats and the D3 targets (Type mismatch, write, overload).
- **Input relations.** `DiagnosticRelationTest`, over all 12 mutations: a blank line changes positions only, and an unrelated problem leaves the existing records unchanged. Ownership analysis requires complete Binding, so an unrelated Binding error withholds ownership records; the relation holds for the same prerequisites. Wording, limits and input order do not enter identity, suppression or order by construction: keys hold no message text, limits apply at finalization after suppression and ordering, and the compiler does not reorder its own work.
- **Outputs.** Commands render the message and code, the location, the excerpt with its label, related locations, omissions, the Note and the Advice. The language server places, merges and sends `relatedInformation` as SPEC §23.4.7 states (`SourceDocumentAndDiagnosticTest`, `LspInputTest`, `LspProjectDiagnosticTest`, `LspProcessTest`). JSON records are prepared but not emitted.
- **Performance.** The D1 build (`5ecffbe9`) and the D4 build alternated on one machine state (`artifacts/benchmarks/diagnostics-d4`). Valid programs allocate about 5 KB more per check (+0.1%, the owner's per-check storage), and their times stay within the run-to-run spread; milestone 22 medians spread 26–42 ms in both builds. The warm rebind and report of Program 37 is unchanged: 120 bytes, 7.1–7.2 ms. Invalid programs pay for recorded identities, derived facts and typed Reasons: 300 errors +10.6% bytes (about 440 bytes per error) and +2.6% time (median of nine); Program 24 +3–6% time. The warm zero-allocation probes pass. LSP measurements allocate the same; D1's deduplication measurement is replaced by ordering and merging (SPEC §23.4.7).
- **Remaining limits.** The open items of §11: parser `UnexpectedToken_Kd` prose, flow refinement reported with Language codes (P33), and ownership failures without the earlier Move or conflicting Loan.

## 10. Diagnostic development workflow (detail)

AGENTS.md lists the required steps. This section gives their detail.

- **Triggers.** A change that adds or modifies a diagnostic, a check that reports diagnostics, recovery or suppression, or diagnostic publication and presentation. Reusing an existing code does not exempt a new reporting path. A reported confusing, missing or misleading diagnostic also starts the cycle.
- **Capture.** A minimal reproducer or a mutation of a known-valid program (§9.2), with the intended failure and independently written expectations. Confirm that the case reaches the intended check; an unrelated earlier error is no evidence. Add a valid counterpart to catch false positives.
- **Review.** Code, severity, category, primary range and label, the factual Reason, related locations, Note and Advice. The message explains the problem on its own; essential facts survive limits; established facts are separated from suggested intent, and advice states its conditions. Inspect representative CLI and language-server output for changed user-visible behavior, including how underlines match explanations.
- **Interactions.** Focused cases for the relevant prerequisites and recovery, independent errors and the input relations (§9.3). The cause stays explained, independent problems stay visible and acceptance does not depend on suppression or presentation. Cover the affected output adapters; a future CSP adapter keeps the same records. Choose cases by the change's impact instead of repeating every test for each diagnostic.
- **Repair and repeat.** Fix missing evidence, misleading explanations, wrong ranges or unwanted cascades at their source, rerun the affected checks and inspect the revised output. Never weaken expectations to match current output, and never count fewer diagnostics as an improvement. Keep the reproducer and its expectations as regression tests.
- **Evidence.** Follow the repository verification workflow; measure (§9.4) when a changed path is hot or a milestone completes. Record the reviewed behavior, intended public-output changes, verification results and remaining uncertainty in commits and `artifacts/verify/`.
- **Completion.** A unit is complete only when its problem is explained accurately and understandably at the right locations, the applicable interaction and output checks pass, and every quality defect observed in the unit is repaired and verified again. Passing tests alone does not establish clarity. Otherwise record the unresolved condition and the next action, without marking the unit complete or claiming support.

## 11. Audit findings (D1)

The D1 audit of the catalog and its reporting sites. Each item names the common rule it breaks and the stage that repairs it; remove an item when it is repaired.

| Finding | Rule | Stage |
| --- | --- | --- |
| 82 of the 99 `UnexpectedToken_Kd` reports pass a description of the syntax position or advice ("setter value parameter", "parenthesize a labeled expression") where the message expects the token; the parser's removed brace-annotation advice is one of them. Identity is unaffected (the text is the syntax context); the repair is a per-site rewrite into token facts, a construct name and Advice. | 2 | Open (parser explanations) |
| Flow refinement is an implementation limit reported with `Language` codes (`UnresolvedBinding_Kd` and `TypeMismatch_Kd` after `is` tests, Programs 33 and 38). Telling a refined use from a genuinely missing member needs the refinement analysis itself. | 3 | P33 |
| Ownership failures (`MovedPlace_Kd`, `UninitializedPlace_Kd` and the Loan conflicts) do not relate the earlier Move, the missing initialization or the conflicting Loan; the cost review of D3 found that this needs source locations in ownership's dataflow state. | Related locations | Open (ownership evidence) |

Repaired in D1: skip helpers reported `UnexpectedTrailingToken_Kd` with an unused argument and used `Template_Kd` as a "no diagnostic" sentinel; `DocumentDesynchronized_Kd` received exception text; source read failures were `GenerationFailed_Kd` (now `SourceReadFailed_Kd`); an incompatible serialized source unit was `UnexpectedToken_Kd` with prose (now `IncompatibleSerializedSource_Kd`).

Repaired in D2b (Binding): the fallback is `PrerequisiteUnavailable_Kd`, reported only when neither Binding nor an earlier phase has an Error; a completed node is no longer a prerequisite; an inaccessible constructor qualifier no longer cascades into `UnsupportedBinding_Kd` at its call; a function passed as an argument (P26, now `UnsupportedBinding_Kd` at the argument) and a grouped bare target such as `x@(ref)` (SPEC §13.5, now a missing Type at the name) were silent rejections that reached the fallback. Implementation limits report `UnsupportedBinding_Kd` through one rule for missed lookups (a cataloged declaration without source, G4: rc/arc intrinsics and `Weak`; a Contract Property requirement through a generic receiver, P24) and at element writes through `uniq` references (G35). An unknown generic Name reports a missing Type at the Name instead of `InvalidTypeFormation_Kd`. An expression bound against a failed expected Type (initializer, assignment, return) and a call whose candidate signature failed are derived from that failure. `NonExhaustiveMatch_Kd` and `NotObjectPayload_Kd` always receive their argument.

Repaired in D2b (startup and control flow): `ControlFlow_Kd` and `ControlFlowWarning_Kd` are replaced by one code per requirement (jump targets, fallthrough, results, labels, null literals, pointer operations, numeric operands, Unsafe Blocks and the discard and loop warnings), reusing `NonExhaustiveMatch_Kd`, `InvalidTry_Kd` and `UnsafeFunctionValue_Kd` where control flow checks Binding's requirement; control-flow checks that read Binding's result are derived from its failures; `InvalidStartupMain_Kd` is derived when the written result Type of main failed; a chained comparison is a recovered form, so Binding no longer reports a mismatch of the parser's guess.

Repaired in D2b (ownership and finalization): ownership reports problem identities; it runs only after complete Binding, so `TransferRequired_Kd`, `UnprovenConstraint_Kd` and `IncompatibleContractImplementation_Kd` never repeat a Binding report of one check. The transitional filter and the former-collection units are removed, which also ends the collisions of embedded library sources. An unverified result without an Error reports one fallback at its first pending control-flow obligation, and an unexplained rejection is a contract fault. A grouping that the indentation ends unclosed reports `MissingExpectedToken_Kd`, like one left open at the end of the source. Related locations are rendered by commands and sent as `relatedInformation` when the client declares it. Eleven catalog codes that no check reported, including `UnsupportedCompileTimeConditionType_Kd`, are removed.

Repaired in D3: records carry a typed Reason from the catalog's argument and evidence schemas, with Types bounded once (a mismatch keeps its differing parts) and omissions counted. `TypeMismatch_Kd` names both Types at the value that does not fit (returns, initializers, expression bodies, operands, conditions, branch results and untyped literals). A write names its target, with Advice for a `let` root. `NoApplicableOverload_Kd` counts and relates its candidates. `InvalidTry_Kd`, `NonExhaustiveMatch_Kd` and `PositionAlwaysFails_Kd` state facts instead of prose, and try has one code per requirement (operand, return, payload). Input and generation failures keep environment text in a bounded Note. A function expression spans from its `func` keyword (Program 26), and a missing closer is an insertion point after the grouping's last token. Cost review: naming the earlier Move or the conflicting Loan needs source locations threaded through ownership's dataflow state, so it is deferred.

Ranges audit follow-up: mismatch recording keeps semantic Types even when short names agree, and display preserves generic arguments and qualifies the designated Kimi position/range identities. Bounded mismatches keep whole differing names. Non-iterable `for` ranges carry the selected entry and boundary Types with conditional repairs; range-shape overload failures retain their actual comparison and keep it in a Note when related candidates are omitted. Warning Advice distinguishes try failure from Abort. `RangeDiagnosticTest` checks public records, prerequisites, independent errors, limits, rebinding and both output adapters; [RANGES_REVIEW.md](RANGES_REVIEW.md) records the audit and verification.

Contract audit follow-up (2026-09-30): `DiagnosticContractTest` covers mixed direct/unresolved and cyclic Errors at one prerequisite key; union, capture and consumption-order selection of related evidence; warning-only and invalidated prerequisites; same-path snapshots; malformed schemas/catalog values and numeric facts; related ranges; and exception record ordering and Note limits. Unit `20260930-114809-063-unit-diagnostics-contract-audit` passes a warning-free Release build, 247 related functional/allocation tests and an unchanged diagnostic snapshot. These fixes do not close the parser, flow-refinement or ownership-evidence items above.

The same Release `Benchmark --diagnostics` workload before/after this unit is retained in `artifacts/benchmarks/diagnostics-audit/{before,after-core}.json` (seven fixed samples after warm-up). Warm rebind/report remains 120 bytes per operation. The 300-error check rises from 4,099,150 to 4,153,422 bytes (+1.3%) to capture and merge prerequisites/evidence and preserve their publication order. Median time is 8.43/8.08 ms; the single pair of runs is not evidence of a speedup.

Input audit follow-up: source consumption records every already-read failure before returning Blocked. Product, dependency and test source decoding use the same Input diagnostics; missing/unreadable or malformed UTF-8 test sources no longer become internal exceptions. Pending/cancelled test reads propagate immediately instead of being swallowed by an earlier product encoding failure. `CheckServiceTest` covers independent failures, syntax between failed inputs, CLI/LSP attribution, successful repairs and cancellation/pending interactions. Unit `20260930-115609-661-unit-diagnostics-input-audit`: warning-free Release build, 119 related tests and unchanged diagnostic snapshot.

Declaration audit follow-up: paired declaration collisions are collected as groups in `Binding.DuplicateDiagnostics.cs`. The first consumed declaration is related evidence of each later declaration; its invalid analysis state rests on a later duplicate check instead of producing an extra direct duplicate record. This covers scope declarations, overloads, aliases, direct conformances and specializations. Function diagnostics retain the parsed signature span and omit the body; variables point at their Name. `DuplicateDiagnosticTest` covers three-way collisions, source ordering, independent errors, source-local positive controls, rebinding, CLI/LSP and zero-allocation warm grouping. Unit `20260930-120724-830-unit-diagnostics-duplicate-audit`: warning-free Release build, 345 tests, 12 specialization native O0/O2 executions and unchanged diagnostic snapshot.
