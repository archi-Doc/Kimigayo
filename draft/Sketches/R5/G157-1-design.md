# G157 (1) design: a body with a validator finding is no consumer's (option (a))

Status: design for review (R5, 2026-11-03). The owner's GO, verbatim 「(a) で設計を始めて」: exclude only the MIR bodies that fail
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

Owner: `MirValidator` (the producer's one validator) at its two entries: `Validate(body, findings)` sets
`body.HasFinding = findings.Count > before` at each of its three exits (the `Binder` and out-of-order early returns included);
`Validate(program)` (`MirValidator.Statics.cs:35`, G191's StaticSlot, added after the build) sets it on the ensuring body beside its
`Add`. Contract text on `MirBody`: "set only by MirValidator; no consumer reads a body that has one". Removes: both all-or-nothing gates and the collector's
header invariant (§6). Not removed: `MirBuilder.Defaults.cs:76-81`'s count difference, which may also count findings of bodies built inside
the template's `Body` call; equivalence is unverified, so it stays (open question Q3).

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
        program.Drops.Close(0, 0, null); // No consumer reads it (R5-M183); its empty entry keeps the table's ordinals.
        continue;
    }
    ... steps 1 and 2 unchanged ...
}
this.effects.Run(program, program.Problems); // 3.
foreach (var body in program.Bodies) { if (Judged(body, rootModules) && !body.HasFinding) { this.Borrows(program, body); } } // 4.
this.origins.Findings(program.Problems); // unchanged: Binding's view, no MIR body (R4-2e).
```

- Finding: `MirDropTable.ends` is positional (`Close` appends "the next body"; `ViewsOf(o)` indexes it), so skipping a body without
  closing an empty entry would shift every later body's A2 table. The empty entry gives `ViewsOf = 0`, `RowsOf` empty.
- A4 (`MirEffects`) enters another body at exactly two places; both test the fact before `cases.Views(callee, subst)`, which reads the
  callee's binders (a `Binder` finding means its views are unresolved):
  - `AddEdge` (`MirEffects.cs:392`; callers `:192` staticEnsure, `:227`, `:236`): `if (callee.HasFinding) { this.found |= Derived; return; }`;
  - `Enter` (`MirEffects.Solve.cs:249`; callers `:223, :237, :303, :332, :417`), after its `body is null` branch: the same two lines.
  The caller composes A4's existing `Derived` class (R4-2c; R4-M10), never an empty summary and never `Unknown`; no new class. The
  excluded body gets no node (`Local` never runs for it), so no `bases` entry is read for it; the caller's other edges and its worklist
  dependencies are unchanged. A `Derived` callee is a constant summary, so the fixed point equals one with a node of class `Derived`.
- A5 enters another body once more: `MirBorrows.Effects.cs:197-206` reads `effects.Summary(initializer, view)` over
  `MirCases.Count(initializer)`. Hunk: `if (initializer.HasFinding) { continue; }` after `:197` (the static contributes no fact: a
  `Derived` summary reports nothing by itself). A5's per-site reads (`Keys`, R4-M92) see the caller's `Derived` site class, and a Loan
  record resting only on it is dropped by rule W's causes (R4-M132; R4-2d), reused unchanged.
- Header: "Must not … analyze a program with validator findings" becomes "read a body with a finding (MirBody.HasFinding): steps 1, 2
  and 4 skip it, A4 and A5 compose a call or initializer of one as Derived".

## 5. Code generation

`Compilation.Generate` (`Core/Compilation.Generation.cs:66`) returns before generation when `MirRecords(null) > 0`, and every finding is
a MIR record; `Accepts` also needs `Analysis.IsVerified`. A program with a finding never reaches `ModuleCodegen`: nothing changes.

## 6. Deletions

- `InstanceCollector.Run`'s `if (program.Findings.Count > 0) return set;` (−4) and the header invariant "a program with validator
  findings has an empty set" (rewritten as "a body with a finding (MirBody.HasFinding) is no template, root or item").
- `MirAnalysis.Run`'s `Findings.Count > 0` return (−4) and the header prohibition above.
- `InstanceCollectorTest.AVersionIsCollectedOnceAndFindingsEmptyTheSet`'s last two lines: the expectation changes because the owner
  chose (a) (stated in the commit); nothing else is weakened.

## 7. Tests (each fails before the fix)

- `MirValidatorTest`: a body the validator rejects has `HasFinding`, a valid one does not, a pooled body rebuilt valid clears it; the
  G191 StaticSlot finding marks the ensuring body.
- `InstanceCollectorTest`: the old row becomes `AVersionIsCollectedOnceAndABodyWithAFindingIsNoItem` (inject by setting the fact on
  `program.Bodies[0]`, as `:477` injects a finding: that body is no item, the other roots stay). New
  `ABodyWithAFindingDropsOnlyWhatRestsOnIt`: root `f` (fact set) requests a generic `h` whose instance is `OversizedLayout_Kd`
  (resting only on `f`); root `k` has an independent `InvalidCLayout_Kd` local (rows as in `.LayoutNeeds.cs`) and an A3b gap A3b
  reports today (the arithmetic shim rows at `:2032`; an `i128` division is still Binding's Opaque until H7); expect `k`'s records
  exactly. One more row pins the gate's place: an `EnclosingInstance` gap at a call into a nested function whose body has the fact
  survives. Before the fix the set is empty, so both fail.
- `MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems` (R4-2f): body X has the fact; body Y's A1 (`MovedPlace_Kd`), A3 and A5
  problems are reported; caller Z of X under a `confined` obligation reports no `UnsatisfiedEffectBound_Kd`, and a Loan record resting
  only on Z's call of X is dropped (R4-2d's row). Before the fix `Problems` is empty. Rerun `TheLibraryHasNoAnalysisProblem`,
  `AWarmReportAllocatesNothing` and `InstanceCollectorTest.AWarmRunAllocatesNothing` (0 B).
- The mir ledger runs before review (R4-2f; a heavy run, registered first): programs with findings newly show other bodies' records;
  each move is named in the commit.

## 8. Size and speed

| File | + / − (lines) |
| --- | --- |
| `Mir/MirBody.cs` (property, reset) | +4 |
| `Mir/MirValidator.cs`, `MirValidator.Statics.cs` | +4 / −1 |
| `Backend/Instances/InstanceCollector.cs` (gate removed, root condition, header) | +1 / −5 |
| `.Templates.cs`, `.Admit.cs` | +3, +2 |
| `Mir/Analysis/MirAnalysis.cs`, `MirEffects.cs`, `MirEffects.Solve.cs`, `MirBorrows.Effects.cs` | +6 / −4, +5, +5, +4 |
| Tests (`MirValidatorTest`, `InstanceCollectorTest`, `MirAnalysisTest`) | about +75 / −2 |

Production about +34 / −10. Cost: one field read per body walk, per admission request and per A4 entry, all on paths already walked;
no allocation, no new pass. No timing run (none is allowed now, and none is needed for a field read).

## 9. Units

One unit, about 50 minutes, two commits, each with its own tests: C1 (R5) the fact (`MirBody`, `MirValidator*`: hold files, R4's
consent recorded) and the collector, with `MirValidatorTest` and `InstanceCollectorTest`, DECISIONS R5-M183; C2 (R4's files, after R4
consents to §4's hunk) the analyses with `MirAnalysisTest`. Unit checks: those classes plus `MirBuildTest`, then the mir ledger. If
C1 passes the hour, C2 becomes its own unit and `MirAnalysis` keeps its gate until it lands (removal condition: C2). No
behavior-preserving commit is needed: no structure moves.

## 10. Open questions

- R4-a: R4-2c says the callee's worklist node keeps its dependency edges. This design gives the excluded body no node at all (its views
  cannot be read) and composes `Derived` at the caller; confirm that this is the intended reading.
- R4-b: the A5 initializer gate (`MirBorrows.Effects.cs:197`) and `MirBorrows.Effects.cs:258`'s `Effect`, which reads only the
  initializer's `Source`: confirm no other A5 path reads another body.
- R4-c: `program.Drops.Close(0, 0, null)` from the driver, or a `MirDrops` member that closes an excluded body (R4's choice).
- Q3 (reviewer): keep `MirBuilder.Defaults.cs:76-81`'s count difference, or replace it with `HasFinding` after showing that `Body` builds
  no other body.
