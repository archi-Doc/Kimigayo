# Change Workflow

This document owns who plans, designs, reviews, implements and lands a change. Principles and roles are in
[AGENTS](../../AGENTS.md) (Implementation Design); checks are in [VERIFICATION](VERIFICATION.md).

## Sessions

| Session | Area | Main work |
| --- | --- | --- |
| **Front** | P1–P5s: Syntax and Binding | Language rules, Types and Contracts, Binding diagnostics, SemanticQuery (Hover, LSP) |
| **Middle** | P6–P10: MIR build, analyses and acceptance | Ownership and borrowing, effects, drop, MIR diagnostics, `MirValidator` |
| **Back** | P11–P13: Backend and runtime | Layout and ABI, code generation, O2 performance, the object runtime, the Kimi library's generated code |
| **Coordinator** | Cross-cutting | Landing on dev (windows), PLAN and DECISIONS numbering, project documents, verification scripts and the benchmark infrastructure |

- Other areas: the Kimi library source (`src/Kimi/Library`) is the Back's; the diagnostics adapter (`src/Kimi/Diagnostics`) and the
  VS Code extension (`src/kimi-ext`) the Front's; `Compiler/Documentation` and the `tests/mir` goldens the Middle's. `Core`,
  `SolutionAndProject` and `Unit` are edited by any session under a board hold and reviewed by the Middle. A `src/Benchmark` series
  follows the component it measures, its infrastructure the Coordinator's.
- **Planning session.** The Front, Middle and Back each plan their area's units: classification, design, delegation, splitting and
  owner reports. A unit across areas is planned by the session owning the component where the cause lies; the others consent to and
  review their hunks; a disagreement goes to the owner. The Coordinator decides no design and plans only its own script and
  benchmark-infrastructure units.

## Documents

- Only the Coordinator edits `AGENTS.md`, `docs/**` and `draft/**`. `docs/examples/**` are test inputs and belong to the
  implementing unit. Where a rule says a unit records something in a document, the Coordinator edits it on the session's request.
- AGENTS, SPEC and IMPL changes get an independent review by an agent who did not write them; no other document needs review.
  Policy documents still change only on the owner's explicit instruction (AGENTS Documentation).
- A session that needs a document change adds a row with the proposed text to the board's document requests. The Coordinator handles
  the requests when a window opens or closes and every 30 minutes. A message only points at a row that needs an answer.
- A DECISIONS entry is drafted by the implementer (state file or request row) and landed by the Coordinator in the window that lands
  the unit.
- The Coordinator pushes documents outside the verification inputs
  ([VERIFICATION](VERIFICATION.md#source-identity-and-commit-association)) directly to dev; documents that are inputs land in a
  window.

## Change classes

| Class | Definition | Flow |
| --- | --- | --- |
| **Minor** | A local change: it stays in one owning component, keeps its contract and invariants, adds no branch, parameter, state or table, and a search finds no other site with the same cause. Its commit states the rule and the search. | Implementation → review |
| **Major** | Every other change. | Design → design review where [Design](#design) step 4 requires one → implementation → review |

- Work prototype first (DECISIONS R7-M3 RP13): iterate an implementation against the tests, then write the design from it. A
  required design review judges that design before the implementation is completed.
- Two candidates or any other special flow run only on the owner's instruction for that unit.

## Design

1. Establish the failing condition and its cause from the code and a reproducer. Label an unverified hypothesis as one.
2. The planning session classifies the change; a reviewer who finds the class wrong reports it as a blocking finding.
3. A Major change gets a short design as one chain: the required meaning (the SPEC rule and the forms it governs) → what the
   representation lacks or wrongly allows → the owning component that decides it → what becomes unnecessary. It lists the sites
   sharing the cause ([CODEMAP](CODEMAP.md)), the options (at least a fix inside the owning component and a change of shared
   structure), the deletions and the expected size and speed.
4. A design review is required for a change to a shared representation or across phases. Another Major change gets one only when a
   design issue must be settled before the implementation is completed, for example: the cause is still a hypothesis; the options
   differ in owning component or representation; the design departs from a DECISIONS or SETTLED entry; the sites sharing the cause
   span several components. The planning session states the reason in the design. Otherwise the one review before completion covers
   the design (DECISIONS Q5).
5. A delegated prompt carries the request, the design, the completion conditions and the checks.

## Implementation

- Implement the design, or for a Minor change the fix its cause requires, with focused tests. While fixing, use incremental builds
  and selected tests only.
- An implementer who finds the design wrong stops and reports to the planning session.

## Working-time limit

A unit's design phase and its implementation phase each stop when the agents' working time on that phase passes one hour
(DECISIONS Q12).

- **Who.** Every agent given design or implementation work, and the planning session's own design work. Reviews follow
  [Repeated reviews](#repeated-reviews).
- **Working time.** Each agent logs its working intervals in its state file (the notes file with its progress and next step) and,
  before each step, sums the phase's logged time, earlier agents' and rounds' included; it stops at the limit. The planning session
  checks the sums. Not counted: heavy runs (DECISIONS R4-M119) and waiting for one, waiting for a window, waiting for the owner,
  usage-limit stops.
- **Stopping.** The agent finishes the running command and starts no new build or test; keeps its work on a `held/…` branch, as a
  WIP commit if the tree builds; removes its heavy-run registration; records in its state file what is done, the findings (causes,
  counterexamples, probes), why the work grew, the open issues and the next steps; and reports to the planning session. Unfinished
  design text and code may be discarded; the findings carry over, and PLAN records the stop as an open issue.
- **Split plan.** From the record, the planning session writes a plan of small, verifiable units.
  - Each unit is classified anew and sized so that each of its phases fits within one hour.
  - Each unit has one owning component and stated completion conditions. A behavior change has tests that fail before it and pass
    after it. A behavior-preserving restructuring shows unchanged results
    ([VERIFICATION](VERIFICATION.md#behavior-preserving-commits)) and comes first.
  - If a unit would make an invalid program valid until a later unit lands, the two merge into dev together.
  - The plan names the earlier blocking findings each unit inherits; a later blocking finding that repeats an inherited finding's
    cause shares a cause with it.
- **Approval.** Stops count over a unit and every unit split from it. After the first stop, the planning session reports the stop
  and the split plan to the owner ([Owner decisions](#owner-decisions)) and starts the split units at once; the owner may stop them.
  After the second stop, it starts them only on the owner's GO. A third stop means the unit does not converge.
- **Sizing first.** Before a unit starts, the planning session sizes it and splits it if it is not expected to fit. Such a split is
  an ordinary planning decision and is not counted as a stop.

## Review

The reviewer starts from the problem, names the general rule and tries a shorter or more general design. It checks that the design
was followed and that no symptom fix (AGENTS) remains. Blocking: a SPEC or contract violation, unsound acceptance, wrong result,
internal fault, regression, departure from the design, symptom fix or wrong class; other findings become open issues. The reviewer
states what is wrong and which rule it breaks, and writes no fix, so it never becomes an author of the design or the implementation.

**Diagnostics scope** (DECISIONS Q13). Of the differences in the records a program reports, only these are regressions; the other
blocking findings above stand:

1. an acceptance move the design does not name: a valid program newly rejected, or an invalid one newly accepted;
2. a new `InternalInvariant_Kd` on any program, or a new Language error on a valid program (one the base does not give);
3. a lost independent record: one the base reports and the change no longer reports, other than a removal by rule W.

- **Rule W** suppresses a record only where its check reads a failed or resting input directly and that input's record, or the
  record it rests on, is still reported (SPEC 23.1.2, "caused only by another reported failure"). Such a removal, and any other
  record added on a program the base already rejects (other than `InternalInvariant_Kd`), is dependent: counted by diagnostic code in
  the commit, never argued and never blocking.
- The commit names DECISIONS Q13 for each expectation it changes under rule W. Q13 never moves an expectation of acceptance or of an
  independent record.
- A form found outside the unit's rows that is none of 1-3 becomes an open issue, and a pre-existing defect stays one.
- A heuristic that a second form breaks is not refined.
  - Where the program is rejected anyway, its record is reported.
  - Where the program may be valid, a located `Unsupported_Kd` marks the analysis limit. On a program the base accepts, that is
    regression 1. Replacing an independent Language record with `Unsupported_Kd` is regression 3.
- A unit has one review, by one reviewer; a fix after REVISE is that reviewer's repeated review. A unit across areas adds each other
  area session's reviewer on its own hunks (DECISIONS R7-M2).

| Verdict | Design review | Implementation review | Next |
| --- | --- | --- | --- |
| **ACCEPT** | No blocking finding. | No blocking finding. | Implementation, or completion. |
| **REVISE** | The design can be corrected without changing its cause analysis or chosen option. | The implementation and tests can be fixed without changing the approved design. | The planning session fixes the design, or the implementer the change. |
| **REJECT** | The cause analysis or the chosen option must be revised. | The cause analysis or the approved design must be revised. | The planning session revises the analysis or the design; after an implementation review, the implementer changes the implementation to match. |

- A fix starts from the reviewed design or change but never keeps a wrong structure for that reason: reuse the reproducer, valid
  tests and unaffected code, and replace the structure the finding concerns where needed.
- The first review presents together every blocking finding it can check. A blocking defect found later is still reported, marked
  as missed earlier, introduced by a fix, a departure from the design, or a newly established fact (such as a constraint the
  implementation revealed).

### Repeated reviews

- The implementer and the reviewer continue as the same agents across reviews; the reviewer of a design review also reviews the
  implementation.
- Each repeated review identifies the reviewed version (a commit or a diff snapshot) and its diff from the previous one, and checks:
  1. the previous findings are resolved;
  2. the diff;
  3. the contracts and use sites the diff affects, including unchanged callers and consumers;
  4. no fix is a symptom fix;
  5. after a design revision, the earlier judgments it invalidated.
- Reviews count per unit, separately for design reviews and implementation reviews, and accumulate: revising the design or returning
  to a design review resets neither count. The diff review after a failed completion check is an implementation review.
- A unit does not converge when two reviews' blocking findings share a cause, when its third design review or third implementation
  review does not ACCEPT, or at its third stop under the [working-time limit](#working-time-limit). The planning session then
  suspends the unit and asks the owner how to proceed ([Owner decisions](#owner-decisions)), unless the cause is record selection
  only, which it decides itself (DECISIONS Q13). The change stays on a `held/…` branch with its evidence and merges into neither dev
  nor main; the unit resumes only on the owner's answer or, for record selection, the planning session's decision.
- A review count is never a reason to ACCEPT a change with a blocking defect.

## Owner decisions

At any point, suspend the unit and ask the owner on finding:

- a contradiction in the specification;
- a significant measured slowdown of compilation or generated code;
- a way to make the specification simpler and cleaner by unifying concepts;
- a unit that does not converge, except on record selection: about diagnostics the owner is asked only acceptance, soundness and SPEC
  questions, and the planning session decides which records a rejected program reports (DECISIONS Q13);
- a unit's second stop under the working-time limit (the first is only reported);
- a disagreement between sessions on a unit across areas.

The report names the SPEC sections, the evidence (for a unit that does not converge, each review's findings and the cause analysis
one level higher; for a stop, its record and the split plan) and the options (a representation, a whitelist, a redesign, a hold or
another) with a recommendation. The Coordinator keeps the questions in the board's owner outbox and records them in PLAN.
Independent units continue.

## Completion and landing

1. After ACCEPT, run the completion checks that [VERIFICATION](VERIFICATION.md#verification-scope) requires on the final inputs.
   Reuse successful evidence for the same inputs, configuration and coverage instead of repeating it. After a failed check, the fix
   gets a diff review and the checks it invalidated run again.
2. Commit, associate the evidence with `scripts/verify-commit.ps1`, push the unit's branch (AGENTS Verification and Delivery) and add
   a queue row on the board.
3. The commit states the root cause, the concept changed, the sites sharing the cause, the deletions, the rejected options and what
   remains unverified.
4. The Coordinator lands queued units on dev in [windows](#windows).

### Windows

A window merges queued units onto dev, verifies them once and lands them together (DECISIONS Q15).

- **Entry.** A unit enters when its review is ACCEPT, its unit checks passed on a base at most two windows old, and its queue row
  names its branch head (the Coordinator's docs commit included) and its expected record and acceptance moves.
- **Moves.** A unit moves acceptance when a program it changes goes from rejected to accepted or back; it moves records when only
  the records of programs that stay rejected change.
- **Size.** At most five units that move records or acceptance, and at most one unit that moves acceptance per area (Front, Middle,
  Back). Documentation-only, test-only and behavior-preserving units fill the window up to eight units.
- **Files.** Units of one window share no file except PLAN's table end and separate DECISIONS entries. An overlap waits for the
  next window, in order of readiness.
- **Run.** The Coordinator merges the units onto dev, builds the solution with warnings as errors and runs the heavy set once: the
  default Session with the diagnostic snapshot against the shared base and every native fixture, the milestones, the CLI harnesses
  and a semantic write. The window's opening line names each unit's expected moves; any other snapshot difference fails the window.
- **Failure.** A failing unit leaves the window and the window runs again. When a failure names no unit, the units are split in
  halves until it does.
- **Close.** The Coordinator associates the evidence with `scripts/verify-commit.ps1`, pushes dev, seeds the shared base and closes
  the board rows. No push that changes verification inputs reaches dev outside a window.
