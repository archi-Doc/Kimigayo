# Kimigayo Implementation Execution

Implement the specified Kimigayo plan through the authorized scope, including verification and necessary documentation updates.

Maintain the plan as the authoritative record of progress, evidence, decisions, remaining work, and next actions.

**Write and maintain the entire plan in English, including progress tables, verification records, decisions, and change records. Preserve source identifiers, commands, paths, and quoted diagnostics exactly. Follow the existing language conventions of other repository documents.**

Do not stop at explaining the plan or announcing progress while authorized, actionable work remains.

## Inputs

- **Implementation plan:** `PLAN.md`
- **Execution scope:** `<optional; defaults to all unfinished in-scope items>`
- **Additional constraints:** `Minimize allocations as much as possible and aggressively optimize for performance and speed. Eliminate unnecessary code.`

Do not assume a particular plan file exists. If the intended plan cannot be identified, ask for clarification rather than selecting or creating one.

This instruction authorizes execution-state updates to the designated plan file. Files under `draft/` still require explicit authorization to edit those files.

If the plan exists only in the conversation, maintain its state through consolidated conversation updates; do not create a file without a specified path.

## 1. Start or Resume

Read the user’s instructions, applicable `AGENTS.md` files, plan state, relevant specification, implementation, tests, and verification setup.

- Follow the specification’s documented authority and precedence rules.
- Inspect the current commit and relevant uncommitted changes.
- Compare plan assumptions, dependencies, states, and acceptance criteria with current code.
- Do not reimplement completed work.
- Do not treat historical successful checks as proof about changed code.
- Run baseline checks where needed to distinguish existing failures from regressions.
- On resumption, assess changes since the previous checkpoint and their effects on completed work and verification.

Update the plan’s current position and briefly report the initial milestone, next change/check, and material blockers.

Preserve user changes. If overlapping changes create uncertainty, reconcile them before editing rather than overwriting them.

## 2. Implementation Cycle

Follow milestone dependencies and use small, coherent changes:

```
Confirm prerequisites and inspect the relevant path
→ Mark the item IN_PROGRESS
→ Implement the smallest coherent change
→ Build and run focused tests
→ Diagnose and fix failures
→ Run necessary regression checks
→ Review acceptance criteria and the diff
→ Update plan state, evidence, and next actions
→ Continue
```

- Reuse existing responsibilities, data structures, naming, error handling, and test conventions.
- Avoid unrelated renaming, broad formatting, speculative abstractions, generalization, and rewrites.
- Combine tightly coupled items only when splitting them would produce an incoherent change; record why.
- Do not mark a milestone complete while its required verification fails.
- Do not advance dependent work past an unresolved prerequisite.
- Continue independent work when possible.
- Do not request repeated confirmation for routine implementation decisions already within the authorized scope.

Use the plan’s stable IDs to preserve requirement/change/test traceability.

## 3. Maintain the Living Plan

Update the plan at:

- Milestone start and completion.
- Material changes to implementation approach, dependencies, or verification.
- Discovery or resolution of blockers.
- Work interruption, handoff, or completion.

Maintain:

```
Current position / Item states / Evidence / Unresolved issues / Next actions / Material plan changes
```

Keep the plan concise and current. Do not copy every edit, command, or transient failure into it.

Use:

```
TODO / IN_PROGRESS / IMPLEMENTED_UNVERIFIED / DONE / BLOCKED / NOT_APPLICABLE
```

Rules:

- Use `DONE` only when acceptance criteria and required verification are satisfied.
- Distinguish implemented changes from completed verification.
- Use `NOT_APPLICABLE` only with a substantive justification, never to hide unfinished work.
- Keep IDs stable and retain the disposition of superseded items.
- Reopen completed items when new evidence invalidates their completion.
- When subsequent changes affect verified behavior, retain the historical result and mark the relevant checks as requiring reverification.
- Do not mark a parent milestone complete while required child items remain incomplete.
- If the plan was externally edited, reread and reconcile it before updating.

**Never reduce requirements or acceptance criteria merely to match the implementation.**

## 4. Deviations, Decisions, and Scope

Adjust implementation details autonomously when the change preserves specification behavior, authorized scope, compatibility commitments, and acceptance criteria. Record the rationale and affected items.

If progress requires an undefined language rule, unresolved specification conflict, substantial redesign, scope expansion, or relaxed acceptance criteria, record:

```
Classification / Evidence / Difference from the plan / Affected items / Recommended response / Required decision / Independent work
```

Investigate questions that can be resolved from available evidence before asking the user. Consolidate necessary questions and pause only dependent work. Silence is not approval.

Once a decision is established, update affected requirements, milestones, tests, and next actions without erasing the previous rationale.

Fix an out-of-scope issue only when it is a necessary prerequisite within the authorized effort. Otherwise, record it under **Out-of-Scope Findings** with evidence and impact.

## 5. Architecture and Implementation Invariants

Use the stages and representations that actually exist. Do not assume or introduce a separate Bound Tree or other conventional layer without a demonstrated need.

Preserve applicable invariants:

- Responsibilities of parsing, Binding, analysis, Lowering, generation validation, and writing.
- Availability and validity of types, symbols, and analysis results before use.
- Ownership, Borrow, Origin, evaluation count/order, Move, and loan lifetime.
- Control flow, result delivery, normal transfers, cleanup, Abort, and nontermination.
- Rebinding, serialization/reload, invalidation, and reused state.
- Separation of language semantics from LLVM and ABI constraints.

Distinguish language errors, currently unsupported generation, and internal inconsistencies.

Removing an `Unsupported` restriction requires confirming that all newly reachable analysis, generation, and validation paths are implemented. Do not broaden acceptance by merely removing a guard.

Minimize unnecessary allocations, repeated scans, retained memory, and avoidable complexity. Use existing measurement infrastructure when performance is affected, record comparison conditions, and do not claim unmeasured improvements.

## 6. Diagnostics and Tests

Report invalid source through the existing diagnostic mechanism at the correct stage, category, and source location.

Avoid unnecessary cascades. Continue safely when recovery is supported; after fatal failure, do not pass invalid state downstream or publish success artifacts.

Fix reproducible crashes caused by relevant malformed inputs at their root. Preserve internal invariant checks; do not hide internal corruption behind catch-all handling or ordinary user diagnostics.

Add or update tests for observable behavior changes and bug fixes:

- Select Positive, Negative, Boundary, Interaction, and Regression cases according to impact.
- For bug fixes, establish a reproducer that fails for the intended reason before the fix when practical.
- Ensure negative tests reach the intended check.
- Reuse existing coverage where sufficient.
- Avoid redundant tests, assertions that merely mirror implementation, and incidental IR formatting dependencies.

Do not conceal failures by:

- Deleting, skipping, or filtering out required tests.
- Weakening assertions or diagnostic checks.
- Changing expectations to match incorrect behavior.
- Downgrading errors to warnings.
- Bypassing validation.
- Adding production behavior conditional on test names or test execution.

If a test is incorrect, justify its correction from the applicable specification and preserve the intended verification responsibility.

## 7. Verification and Failure Handling

Use commands verified against repository configuration and scripts. Distinguish:

```
Build / Managed tests / Fixture or IR generation / LLVM verification / Ordinary native execution / Performance measurement
```

Confirm that:

- Source, configuration, toolchain, and artifacts match the intended verification.
- `--no-build` and similar options refer to an up-to-date build.
- Filters execute the intended tests; zero matching tests are not completion evidence.
- Native checks use the intended newly generated fixtures.
- Shared mutable outputs are not modified concurrently.
- Nonterminating cases have bounded execution and process cleanup.

Start with focused checks and broaden regression coverage according to impact. Do not repeat successful broad checks without relevant changes or unresolved concerns.

Record results as:

```
PASS / FAIL / NOT_RUN / BLOCKED
```

Associate evidence with its target, configuration, and relevant code or artifact state. One stage’s success does not prove later stages.

On failure:

```
Identify the first meaningful failure
→ Establish reproduction conditions
→ Determine its relationship to current changes
→ Find the root cause
→ Apply the smallest justified fix
→ Rerun failed checks
→ Verify affected behavior
```

Distinguish new regressions, pre-existing defects, specification/plan/test inconsistencies, environment limitations, and flaky tests. Do not retry indefinitely until a pass appears.

If required verification cannot run, record the missing evidence, blocker, and exact resumption procedure. Do not weaken the verification requirement.

**Run NativeAOT tests only when explicitly requested. Ordinary native execution is a separate category.**

## 8. Documentation and Workspace Integrity

Maintain clear ownership of information:

- **Plan:** this effort’s progress, decisions, evidence, remaining work, and resumption point.
- **`STATUS.md`:** product-wide implemented support and limitations.
- **Specification:** required language behavior and implementation contracts.
- **README/examples:** supported usage and configuration.

Update documents when the implementation requires it. Do not rewrite the specification to accommodate incorrect implementation, or edit already-correct specification text merely to announce support.

Also:

- Preserve existing user changes.
- Do not edit `draft/` without explicit authorization for the relevant files.
- Inspect generators and use the established generation process for generated files.
- Avoid unrelated version changes; handle necessary ABI/schema changes according to the plan and repository rules.
- Keep detailed execution history in one place rather than duplicating it across documents.

## 9. Completion or Interruption

Before completion, compare the entire authorized scope with the final code, tests, documentation, and plan state.

Confirm:

- Every required item has implementation and verification evidence.
- Final required checks cover the latest relevant changes.
- No unexplained new warnings or diagnostic regressions remain.
- Unsupported boundaries remain explicit.
- The diff contains no accidental edits, debug code, temporary workarounds, unnecessary TODOs, unused additions, or test-only production special cases.
- The plan accurately describes the delivered result.

Before interruption or handoff, record:

- Current milestone and item.
- Implemented but unfinished or unverified work.
- Relevant failures and blockers.
- Pending decisions.
- Exact next implementation and verification steps.

Keep implementation state separate from verification state. Do not claim overall completion while required work or evidence is missing.

## 10. Final Report

Report concisely:

1. **Result** — complete, partially complete, or blocked; behavior delivered.
2. **Changes** — important changes, rationale, and file links.
3. **Verification** — commands, configurations, targets, results, and limitations.
4. **Plan Status** — updated plan reference and milestone states.
5. **Deviations and Remaining Work** — significant decisions, incomplete work, missing verification, and next actions.
6. **Out-of-Scope Findings** — identified issues left outside this effort.

Use **None** where there are no remaining items or findings.

**Update the plan from observed implementation and verification evidence. Do not change the evidence or completion criteria merely to make the plan appear complete.**