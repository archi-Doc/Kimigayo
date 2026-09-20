# Coding Guidelines

- Minimize memory allocations and optimize code for performance wherever practical.
- Update `SPEC.md` and `STATUS.md` as needed to reflect the changes made. Write all updates in English.
- Do not automatically update files in the `draft` folder unless explicitly instructed.
- Do not run NativeAOT tests unless explicitly specified.

# Documentation Responsibilities

- `SPEC.md` and its referenced specification chapters define required language behavior. Implementation limitations must not weaken these requirements.
- The `draft` folder contains proposals. Once finalized, their content is incorporated into `SPEC.md` and its referenced specification chapters. This flow is one-way: do not propagate changes from the formal specification back to `draft`. The formal specification must be self-contained and must not reference or depend on `draft`; do not use `draft` as an authority for required language behavior.
- `PLAN.md` records the current implementation plan: scope, acceptance criteria, item states, dependencies, unresolved issues, and exact next actions.
- `STATUS.md` summarizes product-wide implemented capabilities, verified support boundaries, and remaining limitations. Do not describe planned or unverified work as completed support.
- `PLAN_HISTORY.md` preserves past execution checkpoints, verification results, failures and fixes, decisions, and superseded approaches. Historical instructions and deadlines are not current execution instructions.

Keep current state and next actions in one place in `PLAN.md`. Move superseded execution details to `PLAN_HISTORY.md`, retaining stable IDs, decision rationale, and evidence references. Avoid duplicating detailed history across documents.

Update the relevant documents when implementation changes affect them; do not update every document mechanically. Keep documentation concise and write all updates in English.
