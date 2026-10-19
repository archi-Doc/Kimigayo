# Exploratory common-record protocol model

Standalone design evidence for the selected contract in the main checkout's
`artifacts/handoff/2026-10-20-r4-resume/records/selected-design.md` and the unchanged portions of `design-a.md`.
Base: `48c7acd3d0a1482ec741934286e4afe515980332`. This directory is a new model; it changes neither accepted A5 prototype.

The model captures full typed issue/Reason/fact equality, body-local masks, one Begin/Complete lifecycle, current
known-Language copy-before-hold disposition, and four closed surviving-mask joins. GuardReverse is **proposed**;
DefaultOrigin is the adopted PD11 rule. No rule adoption, O1 approval, family completion, or production integration follows.

The selected category repair is `records/model-category-repair.md` in the main checkout's handoff directory.
`Catalog.cs` is the sole code-to-existing-category table, used by input validation, copy disposition and cover eligibility.
Checked preserves the supplied category; copied Proof and Unsupported remain. Held requires a body-bearing Language
Conflict/Effect input, and its defined reclassification clears known facts. All Unsupported inputs, Checked or Limit,
must already have empty Reason/facts. Malformed input is rejected, not normalized. Inlined provenance requires a body.

`Values.cs` contains immutable symbolic identities and closed values. `Domain.cs` stands in for the producer's validated
identity/position publication. In this finite fixture universe, Borrow/Guard/Point/Access/Obligation identities belong to
body Zero; body Other and NoBody are exercised by Source records. A/B Borrows are declared PreparedInput-rooted, and
Return/Return2 carry those explicitly named Borrows in `_0`. Every declared edge asserts the unproved semantic twin
correspondence; checking its kinds, body, finite source and boundary here does **not** establish it from Binding or MIR.
`Declare` rejects an obligation identity reused for a different semantic position before any recording or Reason comparison.

`Report.cs` owns capture and finalization. Exact typed sets merge; fact sets with distinct witnesses retain separate rows.
The fixed dependency ranks are cleanup/default Borrow, then Origin/default Access, then storage-end. Only final Checked
Language masks cover a subordinate. A valid target with no failed row contributes zero. The public grouping key strips
private finite-chain identity while preserving the complete Origin and captured display recipe, including recipe kind.
Publication keeps full paired explanation sets and decodes cases separately for each body; NoBody has presence `1` and
is never decoded as cases. Full row equality uses typed values, never the text formatter. Sorted output has no insertion IDs.

The selected external fault path is existing `MirProgram.Report(subject, InternalInvariant_Kd)` -> `program.Records` ->
`Compilation.MirRecords`, outside MirReport. The isolated executable represents this with `ProtocolFault`, caught only
by its oracle. It does not add a production collector or recursively record a contract failure. Physical integration of
that path remains unimplemented.

`Fixtures.cs` supplies inputs, and `Expected.cs` supplies separately handwritten complete row/publication expectations.
Expected uses literal shared text and an explicit status matrix only to abbreviate authoring; it reads no fixture/model
values and calls no formatter. The executable writes their complete expansion to `expected.txt` before recording.
There is no bless/update mode. Every permutation of each scenario's input rows is checked against the same full output;
every actual output is preserved, not just its count/hash. Duplicate fact order, case OR, distinct Reasons/chains,
OriginOnly/Type, omitted/closure/Borrow recipes, static field Read/Write/Unknown, witness pairing, independent destruction,
two boundaries, other Borrow, independent write/use/store, held/limited/copied/missing targets, cross-body universes and
NoBody are included. `ProtocolChecks.cs` checks lifecycle/domain faults, same-version reuse and reset invalidation.

The category repair adds complete copied-Proof/Checked-Unsupported and non-covering-Unsupported scenarios. New rejection
checks include Held category/issue mismatches, malformed Unsupported evidence, and NoBody Inlined. Both original Limited
fixture preparations now construct empty Unsupported inputs; the old witness-bearing inputs remain rejection tests.
The first 21 complete expected outputs are unchanged. New tests were run against the unchanged implementation first;
their 720 complete-output failures and 10 input-contract mismatches are preserved in `model-run-04-before`.

`Ablations.cs` contains four deliberately wrong independent controls: first-facts aggregation, span matching, Loan-wide
suppression and coverage using a removed preferred row. Each must disagree with its complete oracle. These are
experimental ablations, not product modes, relaxed expectations, or acceptance checks.

## Run in an isolated copy

Use SDK `11.0.100-rc.2.26504.105` with the installed net10 reference/runtime packs. As the existing prototype instructions
require, copy this directory outside the repository before restore/build: repository `Directory.Build.props` must not apply.
No package dependencies, full solution build, compiler tests or NativeAOT are needed. Serialize source changes and execution.
For example, in a new scratch folder outside the repo:

```powershell
dotnet restore RecordsModel.csproj --source <empty-local-feed> --disable-parallel
dotnet build RecordsModel.csproj --no-restore -c Release --disable-build-servers -p:UseSharedCompilation=false
dotnet bin/Release/net10.0/RecordsModel.dll <new-evidence-output-directory>
```

The output directory must not exist; this preserves prior failed runs. Exit 0 requires every complete output, all four
negative controls, and every protocol check. Evidence includes full expected/actual/negative-control rows, mismatches,
protocol checks and totals. Preserve commands, exits, source SHA-256 manifests before/after and accepted-prototype hashes.

This is a logical model using allocating immutable objects and ordinary collections. It measures neither physical layout,
compilation cost, allocation nor retained memory. Reset tests show logical invalidation/clearing, not GC/liveness. It does
not publish real F4/F9 correspondence, classify A4 effects, prove default template ownership, or settle M69 caller effects.
It models current disposition only for the known-Loan fixtures; no inlined effect conflict fixture asserts a new policy.
All those gates, and the second independent design review, remain open. No production support is claimed.
