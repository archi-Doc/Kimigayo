# G158 (3), R5's prototype-first candidate: design note (WORKFLOW Design step 3)

Branch `claude/r5-g158-proto` (dev 44fee6707 with `origin/claude/bu-g158-tests` 910a5583d; the two `*.CLayoutUses.cs` judge files
unchanged). DECISIONS entry: `DECISIONS-draft.md` beside this note (not in DECISIONS.md; the choice decides which entry lands).

## 1. Required meaning

- SPEC 6.7.1: `#Layout("C")` selects C layout; C layout is unavailable for an open or derived struct, an empty struct and a struct with
  a zero-sized Field (Unit, Never, a zero-length array, a zero-sized struct); a use that needs such a layout is a Language error
  (8.15.3). The Attribute's own errors are its target, argument, spelling, one per fragment, one mode, one storage fragment.
- SPEC 8.15.3: a layout obligation is checked where the program stores, creates, passes or returns a value of the Type by value or needs
  its size, over every instantiation a checked nongeneric body requires (reachable or not); not behind a borrow, raw pointer or object
  handle, nor in a Constraint; the error is at the use. 23.2.2: `check` reports every Language error of the checked set, and a
  check-accepted program never fails `build` with one. 23.1.2: each independent failure is reported.
- The planning answers: a parameter's or result's record is at its use (a), keep gives one or more (b), the argument's record is within
  the call (c), sel judged on published records (d).

## 2. What was wrong

1. Binding decided availability at the declaration: `#Layout("C")` on an open, derived or empty struct, a direct zero-sized Field and a
   written instance with a zero-sized Field anywhere (also behind a borrow or raw pointer, or in a Constraint) were Binding errors that
   invalidated the struct, with dependent `InvalidConstraint_Kd` records at its users (R4-M134 Q8: conservative errors).
2. Binding owned a second zero-size rule (`HasZeroStride`, two callers, omitting Never) beside Layout's, and published
   `CLayoutRejected` so Layout derived Rejected storage; `IsCLayout` meant "selected and available by declaration", so Layout laid an
   open, derived or empty C struct out in Kimigayo mode (Ok).
3. The checked set's layout obligations were judged by the instance collector under MIR code generation only (R5-M41). On the default
   and mir pipelines a written instance was reported eagerly by Binding at the written Type and an inferred one not at all at check: the
   old generator refused it at `build` (`GenerationFailed_Kd` / `GenerationResourceLimit_Kd`), against 23.2.2. Under sel a written use
   was reported twice (Binding's and the collector's, K10b's declared overlap). The same gap held for `OversizedLayout_Kd` (R5-M52's
   deviation).

## 3. Owning components

- **Binding (layout validation)** publishes only what its syntax decides: `AdtDef.CLayout` = `None`, `Selected` (a valid
  `#Layout("C")`) or `Unavailable` (that struct is open, derived or empty), written for every struct in every pass. One fact replaces
  `IsCLayout` and `CLayoutRejected`. SPEC 22.6's `CExchangeable` reads `Selected`.
- **Layout** decides an instance's availability, the size part included: `Unavailable`, or an exactly zero-sized Field of a `Selected`
  struct (a stored Never counts, IMPL 21.1.3), is the CLayout outcome at any depth in the window; the parts are placed all the same, so
  an oversize beside it is still seen (23.1.2).
- **The instance collector** is the one judge of the checked set's by-value uses (`RecordOf`, unchanged). It now runs in every
  `Compilation.Analyze`: the MIR is built, validated and collected on every pipeline (analyzed under `mir` only). `MirRecords` publishes
  the checked set's Language records (`InvalidCLayout_Kd`, `OversizedLayout_Kd`, by the catalog's category) on every pipeline and its
  generation records (limits, capability gaps, broken contracts) under MIR code generation, whose generator they concern; the old
  generator reports its own at generation until G2. So the sel collector and the default pipeline read the same fact through the same
  rule, and nothing reports twice.

## 4. Deletions

`ValidateCLayoutFields`, `CheckCLayoutInstance` with its queue (`cLayoutInstances`) and `storagePrepared`, `HasCLayout`,
`HasZeroStride` (`Binding.Pointers.cs`, deleted), `BindingFailure.InvalidCLayout` (vocabulary −1) with its requirement row and code-map
arm, `AdtDef.IsCLayout` and `CLayoutRejected`, Layout's Rejected mapping for the latter, Binding's eager report (ARCH's K10b ledger row,
DIAGNOSTICS' G2b sentence), the deviations `inferred-clayout-at-check` and `oversized-layout-at-check`.

## 5. Sites and options

- Sites: `IsCLayout`'s readers (Layout's placement, the old pool's `AggregateLayout`, `CExchangeable`, the deleted Field check);
  `HasZeroStride`'s two callers; `BindingFailure.InvalidCLayout`'s three failure sites and its code map; the collector's publication
  (`MirRecords`, one site). No other component judged a C layout.
- Chosen: the fix inside the owning components above (a change of shared structure only in `AdtDef`'s fact).
- Rejected: (a) Binding judging by-value uses on the default pipeline and the collector reading Binding's verdict: a second
  by-value-use rule over syntax and a second zero-size rule; Binding may not read Layout (ARCH dependency rules) and has no checked set
  for inferred instances, so the inferred rows stay unreported at check. (b) The collector's every record at check on the default
  pipeline: G2's OQ5, which publishes codegen gaps and limits the old generator handles. (c) Collecting only for programs that declare
  a C layout: a gate. (d) `IsCLayout` kept beside a second bool for open, derived or empty: two facts for one three-state selection.
  (e) A synthesized constructor exempt from the checked set or from its own layout (see 6): a clause for one form, and a Library
  generates that constructor as a root.

## 6. Results against the counted tests and the kept-apart rows

- Counted: 574 of 600 pass on default, mir (one row covers both) and sel. The 26 failing rows are the empty form's
  (`empty.{none,fieldunused,borrow,raw}` and the ten `empty.*` use rows, each in the default/mir theory and in the sel theory): an
  empty struct has a synthesized constructor (SPEC 6.3.6, "behaves as `init() => ()`"), a nongeneric body of the checked set that
  constructs the struct by value, so the candidate reports `InvalidCLayout_Kd` at the struct (the constructor's location). The rows'
  "declared only: accepted" conflicts with 6.3.6 + 8.15.3 under a sound implementation: a Library generates every root, the synthesized
  constructor included, and its body needs the struct's layout, so accepting the declaration would fail `build` (23.2.2; reasoned from
  `InstanceCollector.Generates` and codegen's CLayout contract, not built). A written constructor is the same use (the rows' own
  `i-init` and `i-toplevel` expect a record at `init`). Proposed: an owner question, the rows kept apart until it is answered.
- Kept apart: the Never rows (part 2) pass on every pipeline: `InvalidCLayout_Kd` beside the Never limit's `Unsupported_Kd` (answer
  (f)), since Layout already counts a stored Never zero-sized and Binding's Never-blind rule is gone. The by-value import rows give the
  import's `Unsupported_Kd` alone (the collector lays out no bodyless import's parameters). Part (1)'s naming rows fail (the message
  names no instance; no catalog change).

## 7. Size and speed

- Size (against dev 44fee6707 with the tests branch; this note and the DECISIONS draft excluded): src +104 −216, tests +80 −133,
  docs +6 −6; `Binding.Pointers.cs` deleted; `BindingFailure` −1 entry.
- Check cost (`Benchmark --analyze-cost`, Generic shape 1/32/64/128, base 44fee6707 against the candidate, one round each on a machine
  loaded by other sessions' runs, so only allocations are exact; `artifacts/benchmarks/r5-g158-proto/`):
  - default (legacy): a fresh check allocates 6.05 → 9.22, 11.71 → 18.20, 17.55 → 27.62, 29.06 → 45.56 MB (+52 to +57 %) and the
    checked compilation retains 4.84 → 6.92 … 21.54 → 32.72 MB (+43 to +52 %); fresh time about ×1.9 and warm Analyze about ×1.5 to
    ×2.3 (noisy). This is the MIR build and collection that `Analyze` now runs on the default pipeline; the warm Analyze still allocates
    the same few hundred bytes (the builder and the collector reuse their storage).
  - mir: +1 to +19 % allocation and retention (the collector beside the MIR it already built); sel: unchanged.
  - Binding (pinned interleaved warm/cold `Bind` A/B, Milestone 1/20/33, three rounds): warm minima equal within 1 %, warm 0 B on
    both; cold −40 B (the deleted queue).
- The default pipeline's added cost is G2's ("MIR, its driver and the collector in every Analyze", OQ5, whose cost the owner accepted
  with G2's measurement) brought forward without the MIR analyses and without publishing anything but the checked set's Language
  records. It is the price of 23.2.2 on the default pipeline: no component of the default pipeline holds the checked set without the
  MIR. If the planning session prefers not to pay it before G2, the default-pipeline rows that need the checked set (every use row:
  the written ones lose their record too once Binding's declaration checks go) would stay failing until G2.

## 8. Completion checks

Evidence: each run's summary, results and pending/snapshot files, and both ledgers, are kept in the main checkout's
`artifacts/verify/r5-g158-proto/`.

- Default Session (`20261027-091945-858-session-r5-g158-default`, in the worktree's `artifacts/verify/`): 25,440 tests, 55 failed,
  2 Not Run (the two Explicit ones the base also leaves): the 26 counted empty rows (§6), the 28 failing kept-apart rows (import 11,
  part (1) 6, their sel theory 11) and one regression, `TypeDagScalingTest.AnInstanceWithAProjectionFieldChecks`: PLAN G271 (Binding
  leaves an associated projection in an instance's stored part uncompleted) makes Layout meet `Src.Element`, which has no row, so the
  collector throws; under sel the same program is already `CheckFaulted_Kd` at base (probed with the base build), and the candidate's
  collection on every pipeline brings it to default and mir. Every other test passes, the Allocation ones included. The tests took
  400 s against the base Session's 298 s (machine load differs).
- sel Session (`20261027-092753-398-session-r5-g158-sel`): the pending gate finds the ledger's 186 cases and 47 out-of-scope
  fixtures unchanged, none passing now or gone, and 55 added: the same 54 judge rows and the G271 regression. No other sel change.
- Diagnostic snapshot (`20261027-094225-173-unit-r5-g158-snapshot`, build and snapshot alone, since the step does not run after a
  failed test step): 0 differences against `shared-base/23c805ea0` (the corpus has no C-layout or oversized program; collecting on the
  default pipeline changed no other record). Every step passed; it is the evidence `verify-commit` associated with 682cf38a5. The
  Sessions cannot be associated: their test steps fail on the judge's rows by design.
- mir ledger (`20261027-093458-319-unit-r5-g158-mir-led`, `-Pipeline mir -TestPurpose Functional` with `KIMI_MIR_DIFF`): the same 55
  failures. Against `shared-base/23c805ea0/mir-diff` (dev without the shared tests) only old-side `Unsupported_Kd` signatures grow
  (2,305 → 2,425 rows), all from the shared tests' programs (the Never limit's and the imports' records): the same ledger run on the
  candidate's own base (1e2fd1b72, dev with the shared tests; its 277 failures are the judge's) gives the identical ledger, 2,425 rows
  in 248 signatures, so the candidate adds no MIR-versus-old difference.
- Unit runs of the affected classes on default, mir and sel are covered by the two Sessions and the mir Functional run on the same
  inputs.
