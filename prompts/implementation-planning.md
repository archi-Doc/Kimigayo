# Kimigayo Implementation Planning

Create an implementation plan for the specified Kimigayo feature or problem.

The plan must serve as both an implementation design and a maintained execution record: requirements, progress, verification evidence, unresolved decisions, and next actions.

**This is a planning phase. Do not begin implementation. Write and maintain the entire plan in English, including progress tables, verification records, decisions, and change records. Preserve source identifiers, commands, paths, and quoted diagnostics exactly.**

## Inputs

- **Target and objective:** `Complete the Kimigayo compiler.`
- **Included and excluded scope:** `Cover all features defined in the finalized specification. Undefined APIs and deferred features are out of scope and should be handled separately.`
- **Relevant specifications, expected behavior, and constraints:** `Minimize allocations as much as possible, and aggressively optimize for performance and execution speed.`
- **Plan output path:** `PLAN.md`

Use information already established in the conversation. If the target is still unclear, ask for clarification instead of selecting a feature yourself.

Do not assume that `PLAN.md` or any other planning file exists. Do not create a file unless an output path is specified.

## 1. Working Boundaries

- Follow the user’s instructions and applicable `AGENTS.md` files.
- Modify only the designated plan file during this phase.
- Preserve existing user changes.
- Prefer the smallest coherent change that satisfies the current specification.
- Respect existing architecture; avoid unrelated fixes, redesigns, abstractions, and speculative generalization.
- Consider allocations, algorithmic cost, retained memory, and existing reuse mechanisms.
- Do not modify files under `draft/` unless editing those files is explicitly authorized.
- Do not run NativeAOT tests or make them mandatory unless explicitly requested. Distinguish NativeAOT from ordinary native execution.

## 2. Repository Investigation

Understand the repository structure, then focus on the target’s processing path and dependencies. Do not read every file indiscriminately.

Inspect:

- Current commit and relevant uncommitted changes.
- Applicable development rules.
- `SPEC.md`, relevant `spec/` chapters and appendices, and related documents.
- `STATUS.md` and any planning material explicitly supplied by the user.
- Relevant implementation, callers, callees, and similar features.
- Tests, shared helpers, fixture generators, and verification scripts.
- SDK, test runner, toolchain, build configuration, and artifact handling.

Support important conclusions with specification sections, file paths and symbols, test names, or actual command results.

Distinguish:

- **Verified fact**
- **Evidence-based inference**
- **Proposed change**
- **Unverified assumption**

Treat historical status and successful test reports as investigation leads, not proof about the current checkout. Do not infer “not implemented” solely from an unsuccessful text search. Mark proposed files and symbols as **new**.

## 3. Requirements and Unresolved Questions

Determine specification authority from the documents’ explicit normative rules, precedence statements, and supersession notices.

Keep language requirements, implementation status, plans, and draft proposals distinct. Existing code and tests are evidence of behavior, not automatically evidence of correctness.

Extract applicable requirements for:

- Accepted behavior and evaluation count/order.
- Invalid inputs, diagnostic stage, and source location.
- Types, name resolution, and scope.
- Ownership, Borrow, and Origin.
- Control flow, result delivery, cleanup, and Abort.
- ABI, runtime, CLI, and artifacts.
- Compile-time guarantees, runtime guarantees, and unsupported boundaries.

Classify unresolved issues as follows:

| Classification | Meaning |
|---|---|
| `SPEC_GAP` | A required language rule or external contract is undefined. |
| `SPEC_CONFLICT` | Applicable specifications contain an unresolved contradiction. |
| `IMPLEMENTATION_MISMATCH` | Current implementation disagrees with the applicable specification. |
| `INVESTIGATION_GAP` | Available investigation or verification is insufficient. |

For each issue, record evidence, affected requirements and milestones, and the information needed to resolve it.

Resolve investigable questions first. Make routine internal design proposals using existing patterns and explain the choice.

Do not invent language behavior or acceptance criteria. Ask consolidated questions when user decisions are necessary, identify dependent work, and continue independent investigation. Silence is not approval.

## 4. Actual Architecture and Current Support

Trace the relevant path from source input to diagnostics, artifacts, and execution.

Describe the stages and representations that actually exist. Relevant areas may include lexing, parsing, Binding, type checking, ownership and control-flow analysis, Lowering, generation validation, IR/LLVM emission, ABI/runtime, CLI, and linking.

**Do not assume a separate Bound Tree or introduce stages merely because they appear in a conventional compiler diagram.**

For each relevant stage, identify:

- Entry points and responsibilities.
- Input/output representations.
- Existing support and limitations.
- Required changes, or evidence that no change is needed.

Inspect serialization, reload, rebinding, invalidation, and cache reuse when affected.

Classify support by requirement, processing stage, and supported input range:

`Implemented / Partially Implemented / Not Implemented / Incorrect or Inconsistent / Unknown`

Keep code inspection, test existence, and current test execution separate. Parsing, Binding, IR generation, LLVM verification, and native execution are distinct support levels.

## 5. Implementation Design

Assign stable IDs:

- `R1…`: requirements
- `M1…`: milestones
- `I1…`: implementation items
- `T1…`: test cases
- `V1…`: verification procedures

Reuse these IDs throughout the plan instead of repeating descriptions.

Define observable success and distinguish:

- Changes included in this effort.
- Existing behavior and infrastructure to reuse.
- Necessary prerequisite work.
- Unsupported behavior that remains unsupported, including its rejection path.
- Unrelated findings.

Divide implementation into dependency-ordered milestones. Prefer small, coherent units that can be built and tested independently.

For each milestone, specify:

- Objective and requirement IDs.
- Dependencies and start conditions.
- Existing files/symbols to change and proposed additions.
- Concrete behavior changes and reusable mechanisms.
- Invariants to preserve.
- Completion criteria and verification IDs.
- Risks and unresolved prerequisites.

If independent verification is impractical, explain the minimum set of changes that must be completed together.

Provide an implementation checklist linked to milestones and requirements. Do not mechanically add syntax nodes, Bound nodes, runtime work, or other changes that the feature does not require.

Include necessary future updates to specification documents, `STATUS.md`, usage documentation, and examples. Do not mark planned support as implemented.

Specify execution steps using references:

`Step / Preconditions / Changes and rationale / Verification / Completion criteria / Failure or blocking conditions`

## 6. Tests, Verification, and Risks

Consider Positive, Negative, Boundary, Interaction, and Regression cases as applicable. Explain omissions briefly; do not manufacture irrelevant tests or exhaustive combinations.

For each test, record:

`Requirement IDs / Input and setup / Expected result / Verification stage and observable evidence / Existing or proposed location`

Provide concrete inputs for representative cases.

Negative tests must reach the intended check and confirm the intended failure reason, stage, and location where relevant. An unrelated earlier error is not sufficient.

Prefer existing helpers and conventions. Avoid redundant tests, implementation-mirroring assertions, and unnecessary dependence on incidental IR formatting or temporary names.

For each verification procedure, specify:

- Working directory and verified command.
- SDK, runner, toolchain, restore/build, and artifact prerequisites.
- Exact target or filter.
- Success criteria and execution order.
- Shared-output constraints and timeout/process-cleanup requirements.

Distinguish:

- Compiler build.
- Managed tests.
- Fixture/IR generation.
- LLVM verification.
- Ordinary native execution.
- Performance measurements.
- NativeAOT, only when explicitly requested.

Ensure downstream verification uses artifacts produced from the intended source and configuration. Serialize operations that share mutable outputs.

Use focused checks during implementation and broaden regression coverage according to impact. Avoid repeated full-suite runs without a change or unresolved concern that justifies them.

Planning-time build/test execution is optional and must serve a specific investigation need. Inspect side effects first. Separate current results, historical evidence, failures, unexecuted checks, and environmental blockers.

For relevant risks, record:

`Cause / Trigger / Impact / Mitigation / Verification / Affected milestone`

Address concrete concerns such as stage responsibilities, evaluation order, ownership and cleanup, validation boundaries, diagnostics, API/ABI compatibility, invalidation, and performance. Do not promise performance improvements or zero allocations without measurement.

## 7. Living Plan Structure

If a path is specified, use that file as the authoritative execution record for this effort. Otherwise, provide the same structure in the conversation.

Separate the plan into:

### Baseline

The objective, specification requirements, agreed scope, and acceptance criteria.

These define success. They must not be weakened to match incomplete implementation.

### Execution State

Maintain:

- Last updated time.
- Relevant code baseline, including uncommitted changes where applicable.
- Planning readiness: `READY`, `PARTIALLY_READY`, or `BLOCKED`.
- Current milestone and executable work.
- Progress table.
- Verification records.
- Unresolved questions and blockers.
- Concrete next actions and resumption instructions.
- Material plan changes and their rationale.

Use these item states consistently:

| State | Meaning |
|---|---|
| `TODO` | Work has not started. |
| `IN_PROGRESS` | Work is underway. |
| `IMPLEMENTED_UNVERIFIED` | Changes are present, but required verification is incomplete. |
| `DONE` | Acceptance criteria and required verification are satisfied. |
| `BLOCKED` | Progress requires a decision, dependency, or environment change. |
| `NOT_APPLICABLE` | The item is unnecessary, with an explicit justification. |

Progress table:

`ID / State / Completion evidence or remaining work / Next action`

Verification record:

`Verification ID / Target and configuration / Relevant code or artifact state / Result / Evidence / Reverification needed`

Use verification results:

`PASS / FAIL / NOT_RUN / BLOCKED`

Do not initialize items as `DONE` without adequate evidence. Historical success must not be recorded as a current run.

Keep IDs stable. Retain removed or superseded work with its disposition and rationale rather than silently deleting it. Record significant decisions, not every command or edit.

The plan tracks this effort; `STATUS.md` tracks product-wide support. Avoid duplicating detailed execution history across documents.

## 8. Final Review and Output

Before finishing, verify:

- Referenced files, symbols, diagnostics, and commands exist or are explicitly proposed.
- Existing support is not scheduled for redundant implementation.
- All in-scope requirements have implementation and verification coverage.
- Every change has a requirement or necessary prerequisite.
- Milestones have usable completion criteria.
- Gaps have explicit effects and resolution conditions.
- The plan supports both initial execution and resumption without inventing specifications.

Use this structure:

1. **Goal and Scope**
2. **Execution State**
3. **Current State and Architecture**
4. **Specification Requirements**
5. **Gaps and Decisions**
6. **Milestones**
7. **Implementation Checklist**
8. **Test Plan and Verification Records**
9. **Risks and Implementation Notes**
10. **Execution Order and Next Actions**
11. **Definition of Done**
12. **Plan Changes and Out-of-Scope Findings**

Keep the plan concrete and proportional to the task. Use cross-references instead of repetition.

**A completed plan is not a completed implementation. State which work is ready to begin and which work remains blocked.**
