- **R5-M1xx C layout is an error only at a use that needs it; the checked set's layout obligations at check on every pipeline**
  (2026-10-27, R5's prototype-first candidate of G158 (3), a Full change under DECISIONS Q11, number assigned at the choice;
  supersedes R5-M26's `IsCLayout` meaning and its readers, R5-M55's `CLayoutRejected`, its Rejected mapping and the declared
  overlap removed at G2b, R5-M29's "C mode from `AdtDef.IsCLayout` ... since Binding rejects those", and R5-M41's "on the default
  pipeline and under `mir` alone the collector does not run" for the checked set's Language records; applies R4-M134 Q8) SPEC 6.7.1
  makes C layout unavailable for an open, derived or empty struct and a struct with a zero-sized Field, and 8.15.3 makes only a use
  that needs the layout the error, at that use, over the whole checked set, written or inferred; 23.2.2 makes `check` report it.
  - Binding publishes the declaration's part once: `AdtDef.CLayout` (`None`, `Selected` by a valid `#Layout("C")`, `Unavailable`
    when that struct is open, derived or empty), written by `ValidateLayoutFragments` for every struct in every pass; the attribute
    keeps only its own errors (target, argument, spelling, one per fragment, one mode, one storage fragment). It replaces `IsCLayout`
    and `CLayoutRejected`. `CExchangeable` (SPEC 22.6) reads `Selected`, so `ref/S` of an unavailable or zero-sized S is the import's
    `Unsupported_Kd`.
  - `Layout` decides an instance's availability: `Unavailable`, or an exactly zero-sized Field of a `Selected` struct (a stored Never
    included, IMPL 21.1.3), is the CLayout outcome at any depth in the window, its parts placed all the same (`Fold.Unavailable`).
  - The instance collector stays the one judge of a by-value use (`RecordOf`, unchanged) and runs in every `Compilation.Analyze`:
    the MIR is built, validated and collected on every pipeline, analyzed only under `mir`. `Compilation.MirRecords` publishes the
    checked set's Language records (`InvalidCLayout_Kd`, `OversizedLayout_Kd`) on every pipeline and its generation records (limits,
    capability gaps, broken contracts) under MIR code generation, the old generator reporting its own at generation until G2.
  - Deleted: `ValidateCLayoutFields`, `CheckCLayoutInstance` with its queue and `storagePrepared`, `HasCLayout`, `HasZeroStride`
    (`Binding.Pointers.cs`; Layout's zero-size rule's second copy, which omitted Never), `BindingFailure.InvalidCLayout` with its
    requirement row and code map, `CLayoutRejected` and its Rejected mapping, the eager report (ARCH's K10b ledger row, DIAGNOSTICS'
    G2b sentence), the deviations `inferred-clayout-at-check` and `oversized-layout-at-check` (an oversized layout is the same
    cause's other Language record and rejects at check on every pipeline too).
  - Declared: an empty `#Layout("C")` struct has a synthesized constructor (SPEC 6.3.6, behaving as `init() => ()`), a nongeneric
    body of the checked set that constructs the struct by value, so the struct is `InvalidCLayout_Kd` at its declaration even when
    nothing else uses it; a Library generates every root, that constructor included (`InstanceCollector.Generates`), so accepting it
    would let `build` meet the CLayout outcome after acceptance (23.2.2; reasoned from the code, not built). The shared rows'
    "declared only: accepted" for the empty form conflicts with this (an owner question; 26 counted rows).
  - Rejected: Binding judging by-value uses on the default pipeline (a second by-value-use rule over syntax and a second zero-size
    rule; Binding may not read Layout, and it has no checked set for inferred instances); the collector's every record at check on
    the default pipeline (G2's OQ5: codegen gaps and limits the old generator handles); collecting only for programs declaring a C
    layout (a gate); keeping `IsCLayout` beside a second bool (two facts for one three-state selection).
  - Cost (`Benchmark --analyze-cost`, Generic 1/32/64/128 against 44fee6707; `artifacts/benchmarks/r5-g158-proto/`): a fresh default
    check allocates 52-57 % more and retains 43-52 % more (the MIR and the checked set, G2's cost without the MIR analyses), about
    ×1.9 in time on a loaded machine; `mir` +1-19 %; sel unchanged; warm `Bind` unchanged (pinned A/B), cold −40 B.
