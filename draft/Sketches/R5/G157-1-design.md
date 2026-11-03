# G157 (1) design: a body with a validator finding is no consumer's (option (a))

Status: revision 1 for re-review (R5, 2026-11-03; review 1 REVISE, table at the end). The owner's GO, verbatim 「(a) で設計を始めて」: exclude only the MIR bodies that fail
validation; judge and collect every other body. Classification: Major with a design review (three components). The design adds
**one** published per-body fact (a `bool` on `MirBody`); no IR form, record kind or A4 summary representation changes, so the class
stands. R4's constraints are cited as "R4, 2026-11-03" (items R4-1 to R4-3, with letters for item 2).

## 1. Required meaning

- SPEC 23.1.2: each independent failed requirement is reported, even when another check fails. AGENTS: consumers trust validated IR;
  a broken contract is `InternalInvariant_Kd` without recovery; a dependent record is suppressed only where its check reads a failed
  input directly whose record is reported.
- Forms: body X has a finding, unrelated body Y has an independent record → Y's record is reported. A record that reads X (X's own
  A3b checks and layout obligations, an instance X alone requests, a summary composed from X) is X's finding's dependent and is not
  made. Acceptance never moves: any finding is already a MIR record, so check refuses the program (`Compilation.Analysis`) and
  generation never starts (§5). Only the selection of other records changes (DECISIONS Q13: a lost independent record is the
  regression this fixes).

## 2. Representation

| Option | Verdict |
| --- | --- |
| **A. `MirBody.HasFinding`**, set by `MirValidator` where it adds a finding located in that body, cleared by `MirBody.Reset` | **Chosen.** Every consumer already holds the `MirBody` before reading it; pooled with the body, no storage, warm rebuild allocation-free; named like `HasOpaque`. |
| B. A body ordinal on `MirFinding` | Each consumer would re-derive a per-body set from `Findings` (R4-2a forbids it; a second copy of the rule per consumer). |
| C. A set or column by ordinal on `MirProgram` | A side table beside the body with its own grow and clear; every reader needs the program as well as the body. R4-2a's "for example" is met by A, the validator's per-body publication. |
| D. `MirProgram`'s accessors hide broken bodies | Rejected: `null` already means "no body" (`Bodyless`, published rows), so a broken callee would silently take the bodyless path: a fallback that hides a failure. A filtered `Bodies` breaks the per-ordinal bookkeeping both consumers need (§3 `starts`, §4 `MirDropTable`). |

Owner: `MirValidator` (the producer's one validator) at its two entries. `Validate(body, findings)` becomes a wrapper with one
assignment: `var count = findings.Count; this.Check(body, findings); body.HasFinding = findings.Count > count;` (today's body, early
returns included, moves unchanged into the private `Check`). `Validate(program)` (`MirValidator.Statics.cs:35`, G191's StaticSlot, added
after the build) sets `body.HasFinding = true` beside its `Add`. `MirBody.Reset` clears it.
- `MirBody`'s header names the verdict among what it carries: "and the validator's verdict (HasFinding), which only MirValidator
  writes; no consumer reads a body that has one". The property is `{ get; internal set; }`.
- Test seam: tests inject the fact through that setter on a built program, as the old row injected a finding into `Findings`; the
  validator's own writes are pinned by `MirValidatorTest` (§7). No other production writer exists (a grep in review).
- Removes: both all-or-nothing gates, the collector's header invariant (§6) and the second copy of the rule in
  `MirBuilder.Defaults.cs:76-81` (B2): `Body` is not reentrant (it resets the builder-wide body, locals, targets and scopes) and only
  `Validate(body, …)` at `MirBuilder.cs:275` adds findings during it, so `this.program.Findings.Count > findings` is
  `this.body.HasFinding`; the `findings` local goes.

## 3. The collector (R5's, `Backend/Instances/`)

A body enters the collector only at three places; each tests the fact once, and every other read is of an admitted item's body:

| Path | Enters through | Gate |
| --- | --- | --- |
| Templates, A3b definition-stage checks (`CheckOperation`, `CheckStatic`), need listing (`ListNeeds`) | `Templates`' walk of `Bodies` (`.Templates.cs:77-95`) | the walk: a body with a finding records an empty range (`starts[o]` still written, so `starts[o+1]` stays a valid end) and is not read |
| Roots | the root loop (`InstanceCollector.cs:148`) | `!body.HasFinding &&` in its condition |
| Callee (`.Callees.cs:53-54`), witness (`.Witnesses.cs:94`), VTable slot (`.Objects.cs:102` via `Implementation`), default template (`.Edges.cs:165`), Dictionary construction and placeEntry's append (`.Callees.cs:132` via `Declared`), static initializer (`.Glue.cs:64`), destructor (`.Glue.cs:159`), Closure (`.Callees.cs:34`) | `Admit` (`.Admit.cs:67`) | `if (target.HasFinding) return -1;` after the two checks that read only the request (an instance without its substitution; `CarriesInstance`), before `Find`, `Instantiable`, `Grows` and `View`, which read the body |
| Edges, Layouts (`LayOutLocals`, `LayOutNeeds`), Objects (`.Objects.cs:51`), `Rest` (`.Edges.cs:184`), `Declaration` (`.Admit.cs:317-339`), `Subject`, code generation | an item's `BodyOrdinal` | none needed: only the root loop and `Admit` set it |

- Rejected alternatives for one gate: roots through `Admit` (a root would then count toward `Grows`' identity counts and `Work`, a
  behavior change outside this unit); merging the root loop into the walk (a restructuring that needs its own behavior-preserving
  commit with snapshot and fixture evidence, a heavy run, for eight lines).
- A refused request records nothing. `Report`'s dependent rule (an `InternalInvariant_Kd` dropped in an incomplete Binding) is not
  involved and needs no change: the record the refusal rests on is the finding, which `Compilation.MirRecords` publishes. The edge's
  target is -1, as for any refused edge ("a refused edge is dropped and collection continues").
- Kept independent records: a caller's own A3b checks and layout obligations; a request record that reads only the request (the
  `EnclosingInstance` gap, an instance without its substitution) even when its target is broken. Dropped: X's own records and every
  instance X alone requests (they rest on X's request).
- Warm zero allocation: a field read per body and per request on paths already walked.

## 4. The analyses (R4's, `Mir/Analysis/`; the concrete hunk for R4's consent, R4-1, R4-3)

`MirAnalysis.Run`, replacing the `Findings.Count > 0` return (R4-2b):

```cs
foreach (var body in program.Bodies)
{
    if (body.HasFinding)
    {
        this.drops.Exclude(program); // No consumer reads it (R5-M183): an empty entry keeps the table's ordinals.
        continue;
    }
    ... steps 1 and 2 unchanged ...
}
this.effects.Run(program, program.Problems); // 3.
foreach (var body in program.Bodies) { if (Judged(body, rootModules) && !body.HasFinding) { this.Borrows(program, body); } } // 4.
this.origins.Findings(program.Problems); // unchanged: Binding's view, no MIR body (R4-2e).
```

- `MirDropTable.ends` is positional (`Close` appends "the next body"; `ViewsOf(o)` indexes it), so a skipped body must still close an
  entry or every later body's A2 table shifts. `MirDrops`, the table's one writer (`End`, `MirDrops.cs:270`), gains
  `Exclude(MirProgram program)` with the contract "a body with a finding closes an empty entry (no rows or points, no flags,
  `ViewsOf` 0)" (R4-c); the driver never writes `program.Drops` itself.
- A4 (`MirEffects`) enters another body at three places, each through `cases.Views(body, subst)`, which reads the body's binders (a
  `Binder` finding means its views are unresolved): `AddEdge` (`MirEffects.cs:395`), `Enter` (`MirEffects.Solve.cs:279`) and `Owe`
  (`MirEffects.Solve.cs:406`, an obligation owed by the definition's own body, B1). One private A4 member replaces the three reads:

  ```cs
  // The views of a body a call, a destruction or an obligation enters that its substitution selects; none for a body with a finding,
  // which no consumer reads (R5-M183): what rests on it is that finding's dependent (Derived, R4-M10).
  private ulong Views(MirBody body, Subst? subst)
  {
      if (body.HasFinding)
      {
          this.found |= Derived;
          return 0;
      }

      return cases.Views(body, subst);
  }
  ```

  Every path, by the code: `AddEdge` stores an edge with views 0, which `Compose` (`MirEffects.Solve.cs:160`, a loop over the mask)
  replays as nothing, and the caller's local classes carry `Derived`; `Enter` composes nothing and its summary carries `Derived`;
  `Owe`'s own path (`perView`) loops over mask 0, so `Verdict` never runs and the obligation on the excluded definition makes no record;
  `Owe`'s other path calls `Enter`, then `Verdict` with `Derived` only, and `Outcome` (`MirEffects.cs:69`) maps `Derived` to None: no
  record, no `Unknown`, no `Limit`. This gives R4's required outcome on every path with one gate, so it is preferred over R4's
  `perView = own && body is { HasFinding: false }`, which still needs `Enter`'s and `AddEdge`'s gates (three tests instead of one).
  The excluded body gets no node (`Local` never runs for it) and no `bases` entry is read for it (answered R4-a). The `Derived` that
  `Owe`'s own path leaves in `found` is reset by every later reader (`Owe`'s loop, `Evaluate`, `Local`).
- A5's per-site reads (`Keys`, R4-M92) see a call of an excluded body as a `Derived` site class. `Derived` reports nothing by itself
  through `Met` and `Destroying`: `MirEffects.Outcome` maps it to None and `Decided` reads only keys and `Unknown` (`MirBorrows.Effects`'
  header invariant, R4-M10) (F3; rule W's `Derived` in `MirBorrows.Causes` is a candidate test, not this path).
- A5 reads another body's summary once more, in the initialization proof (`Proved`, `MirBorrows.Effects.cs:179-215`, over
  `effects.Summary(initializer, view)`). Skipping such an initializer would read it as "produces nothing" and let `Met` (`:125`) or
  `Destroying` (`:167`) report a `ComparisonLoanConflict_Kd` resting on it; treating it as proving would fire their `Limit`
  (`Unsupported_Kd`, `:121`, `:162`), also resting on it (F2). Decided in one place, the proof's result:
  - `Proved` returns `bool?`: true where an initializer without a finding proves the effect; else null where an initializer it consults
    (one that holds a candidate and may be first-accessed) has the fact; else false. The test sits before the view loop, so
    `MirCases.Count` of such a body is never read; immutable statics are covered, since they always pass the first-access test.
  - `Unproved` returns `(MirFactKind? Fact, bool Rests)`: in its order (write, `Unknown`, read), the first kind whose proof is null
    gives `Rests`; else the first unproved kind; else none.
  - `Met` returns before its conflict record and its Limit when `Rests` (the rest of `Met` decides the same root at the same site);
    `Destroying` makes no record when `Rests`. No parameter steers either. `Effect` (`:258`) reads only an initializer's `Source`.
- Header: "Must not … analyze a program with validator findings" becomes "read a body with a finding (MirBody.HasFinding): steps 1, 2
  and 4 skip it; A4 composes a body with one as Derived (Views); the initialization proof that consults one rests on it".

## 5. Code generation

`Compilation.Generate` (`Core/Compilation.Generation.cs:66`) returns before generation when `MirRecords(null) > 0`, and every finding is
a MIR record; `Accepts` also needs `Analysis.IsVerified`. A program with a finding never reaches `ModuleCodegen`: nothing changes.

## 6. Deletions

- `InstanceCollector.Run`'s `if (program.Findings.Count > 0) return set;` (−4) and the header invariant "a program with validator
  findings has an empty set" (rewritten as "a body with a finding (MirBody.HasFinding) is no template, root or item").
- `MirAnalysis.Run`'s `Findings.Count > 0` return (−4) and the header prohibition above.
- `MirBuilder.Defaults.cs:76-81`'s count difference and its `findings` local (B2).
- `InstanceCollectorTest.AVersionIsCollectedOnceAndFindingsEmptyTheSet`'s last two lines: the expectation changes because the owner
  chose (a) (stated in the commit); nothing else is weakened.

## 7. Tests (each fails before the fix)

- `MirValidatorTest`: a body the validator rejects has `HasFinding`, a valid one does not, a pooled body rebuilt valid clears it; the
  G191 StaticSlot finding marks the ensuring body. A default template with a finding stays `Failed` (B2; the defaults rows that pin
  `Failed` today).
- `InstanceCollectorTest`: the old row becomes `AVersionIsCollectedOnceAndABodyWithAFindingIsNoItem` (the fact injected on
  `program.Bodies[0]` where `:477` injected a finding: that body is no item, the other roots stay). New
  `ABodyWithAFindingDropsOnlyWhatRestsOnIt`: root `f` (fact set) requests a generic `h` whose instance is `OversizedLayout_Kd`
  (resting only on `f`); root `k` has an independent `InvalidCLayout_Kd` local (rows as in `.LayoutNeeds.cs`) and an A3b gap A3b
  reports today (the arithmetic shim rows at `:2032`; an `i128` division is still Binding's Opaque until H7); expect `k`'s records
  exactly. One more row pins the gate's place: an `EnclosingInstance` gap at a call into a nested function whose body has the fact
  survives. Before the fix the set is empty, so both fail.
- `MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems` (R4-2f), one theory, each row with the fact on body X:
  1. body Y's A1 (`MovedPlace_Kd`), A3 and A5 problems are reported;
  2. caller Z of X under a `confined` obligation reports no `UnsatisfiedEffectBound_Kd`, and a Loan at Z's call of X (a `Derived`
     site) gives no record (F3's path);
  3. X itself owes `confined`: no record (B1, `Owe`'s own path);
  4. a static whose initializer is X: its Field under a live Loan at an effect site gives no record, while an independent conflict in
     the same body is reported (F2).
  Before the fix `Problems` is empty, so rows 1 and 4 fail; rows 2 and 3 pin the dependent rule. Rerun
  `TheLibraryHasNoAnalysisProblem`, `AWarmReportAllocatesNothing` and `InstanceCollectorTest.AWarmRunAllocatesNothing` (0 B).
- The mir ledger runs before review (R4-2f; a heavy run, registered first): programs with findings newly show other bodies' records;
  each move is named in the commit.

## 8. Size and speed

| File | + / − (lines) |
| --- | --- |
| `Mir/MirBody.cs` (property, reset, header) | +5 |
| `Mir/MirValidator.cs` (wrapper), `MirValidator.Statics.cs` | +8 / −1, +1 |
| `Mir/Build/MirBuilder.Defaults.cs` | −1 |
| `Backend/Instances/InstanceCollector.cs` (gate removed, root condition, header), `.Templates.cs`, `.Admit.cs` | +1 / −5, +3, +2 |
| `Mir/Analysis/MirAnalysis.cs`, `MirDrops.cs` (`Exclude`) | +6 / −4, +4 |
| `MirEffects.cs`, `MirEffects.Solve.cs` (`Views`, three reads replaced) | +11 / −1, +0 |
| `MirBorrows.Effects.cs` (`Proved`, `Unproved`, `Met`, `Destroying`) | +16 / −6 |
| Tests (`MirValidatorTest`, `InstanceCollectorTest`, `MirAnalysisTest`) | about +100 / −2 |

Production about +57 / −18. Cost: one field read per body walk, per admission request, per A4 entry and per initializer the proof
consults, all on paths already walked; no allocation, no new pass. No timing run (none is allowed now; a field read needs none).

## 9. Units

Two units now (the analyses' part grew with B1 and F2, so one unit no longer fits the hour):
- **U1 (R5, about 40 minutes):** the fact (`MirBody`, `MirValidator*`: W1 hold files R4 consented to; `MirBuilder.Defaults.cs`, a hold
  file outside that consent, needs R4's consent too), the collector, `MirValidatorTest` and `InstanceCollectorTest`; DECISIONS R5-M183
  names R4's consent and states the analyses' rule for U2. U1 alone meets H7's prerequisite (A3b's records survive). `MirAnalysis`
  keeps its all-or-nothing gate until U2 (removal condition: U2; recorded in PLAN).
- **U2 (R4's files, after R4 consents to §4's hunk, about 45 minutes):** the driver, `MirDrops.Exclude`, A4's `Views`, A5's proof
  result, `MirAnalysisTest`'s four rows; then the mir ledger before review.

Unit checks: the named classes plus `MirBuildTest`. No behavior-preserving commit: the `Check` extraction moves one member's body
inside the behavior change, and no other structure moves.

## 10. Questions

Answered (review 1):
- R4-a, yes: the excluded body has no node; a constant `Derived` at the caller equals a node of class `Derived`, and the caller's other
  edges and dependencies are unchanged.
- R4-b: in A5, besides `Proved`'s summary read (`:197-206`), only `:185` (`ensured`, a reference compare) and `:258` (`Source` only)
  touch another body; `MirBorrows.Calls` and `CallRows` read Binding's facts. In A4, `Owe` is the third entry (B1).
- R4-c: a `MirDrops` member (`Exclude`), as §4 states.
- Q3: replaced (B2).

Open: none for the design; U1 needs R4's consent for `MirBuilder.Defaults.cs` (one line).

## Revision 1 (review 1, REVISE)

| Finding | Fix |
| --- | --- |
| B1 = F1: `Owe` reads an excluded body's views and base | §4: one A4 `Views` member replaces the three `cases.Views` reads; zero views and `Derived` give no `Verdict` and no record on every path; MirAnalysisTest row 3 |
| B2: Defaults' count difference is a second copy | §2, §6: replaced by `this.body.HasFinding` in U1 |
| F2: the initializer `continue` makes a conflict or Limit resting on it | §4: `Proved` returns `bool?`, `Unproved` carries `Rests`, `Met` and `Destroying` make no record; row 4 |
| F3: rule W's causes is not the path | §4: `Outcome` maps `Derived` to None and `Decided` reads only keys and `Unknown` (R4-M10) |
| R4-c: the driver writes the table | §4: `MirDrops.Exclude` with its contract |
| Non-blocking: wrapper, test seam, header text | §2 |
| Sizes and units | §8, §9: about +57 / −18; two units |
