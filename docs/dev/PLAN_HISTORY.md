# Kimigayo Plan History

Summaries of the anchors other documents link, maintained only on explicit request under [AGENTS Documentation](../../AGENTS.md#documentation). Everything else (sessions, closed rows, evidence paths and measurements) is in git history. Current work belongs in [PLAN.md](PLAN.md), support boundaries in [STATUS.md](../STATUS.md). Historical statements describe their checkpoints, not current support; entries before 2026-10-15 keep their original Milestone IDs ([implementation-order catalog](../../tests/milestones/README.md#implementation-order)).

## Closed open issues

PLAN §7 rows closed from 2026-11-01 on; only the rows other documents link keep a summary here. A closed `PLAN Gnnn` cited elsewhere resolves in git history (`git log -S "Gnnn" -- docs/dev/PLAN.md docs/dev/PLAN_HISTORY.md`); DECISIONS and the commits hold the details.

<a id="g136-landed"></a>
- **G136's landed items (R4):** V16's per-view clearing for the pooled int-valued analysis tables (`MirTable`, DECISIONS R4-M146); a Value call whose signature does not number its operands is the located `InternalInvariant_Kd` (R4-M148). The other-contract tables stay in PLAN.
<a id="g147-old"></a>
- **G147 (2)'s old-generator halves and (8i) (R4 with BU, 2026-10-31):** `vals[0].bump()` and a whole `uniq/string` element compared build and run under MIR code generation (G11a's probe); (8i)'s pair-carrier self-store rows are the borrow case's Content row (DECISIONS BU-49).
<a id="g157-census"></a>
- **G157 items, G289 (2) (R5's census of 2026-11-01):** closed as stale: (22), (34), (49), (50) and the old-generator clauses of (7), (37), (40), (45) (G2–G4, G7, R5-M131); G289 (2) by G2; G205's and G150's R5 conditions lapsed. The later G157 closures follow in git history.
<a id="g160-landed"></a>
- **G160's landed items (R4 and BU):** N2 decided in SPEC (DECISIONS R4-M131: a written raw layer without an Origin retains every other argument, R′); BU's rows (BU-40, `RetentionKind.Chain`); A5's Chain closure (R4-M138); the unread raw readers deleted (BU-39).
<a id="g168"></a>
- **G168, G168b (R5; batches 80 and 81):** an unsupported `Field`/`Reborrowed` Opaque site names only the receiver's root (R5-M84); a selection Binding resolved through an SPEC 8.2.3 identity premise is one located `Unsupported_Kd` (the owner's 「(a) 今行う」, BU-72) until G170's identity query (G355). N1 stays in PLAN.
<a id="g191"></a>
- **G191 (R4; DECISIONS R4-M121):** `MirValidator` checks the builder's static contracts (StaticInit bodies, ensured static roots) and the four consumer guards are deleted. The review's N1–N3 stay in PLAN.
<a id="g232"></a>
- **G232 (BU; bu67):** the fixed-expected clause and `StoredReborrow` read `Binding.StoresExclusiveReference`, so a stored `objuniq/T` at a fixed expected Type Reborrows as `uniq/T` does (SPEC 3.5.1, 10.2.2), Dictionary literal values included. The `h.o.x` limit stayed in PLAN's G220–G233 row.
<a id="g252"></a>
- **G252, BU's part (bu49, DECISIONS BU-45):** the payload rule reads `Binding.BareAcquire`, so `.Some(value)` of an `s/T` Place is acquired by case on both pipelines. G245's other forms stay in PLAN.
<a id="g255-landed"></a>
- **G255 (C1) and (C3)'s Binding fact (BU; BU-40, BU-42):** `dictionaryStorage<K, V>`'s result is Untied when `V` holds an unstated raw layer; `CallRetention.ReferentStores` kept by the owner (2026-10-26, 「(1) 最初の修正版を維持 (推奨)」; R4-M143). (C2) and (C3)'s A5 read stay in PLAN.
<a id="g264-landed"></a>
- **G264's landed parts (R4 with BU, to 2026-10-31):** U0 (BU-47, `TyCtxt.RetainsErasedDestruction`); S1 under the owner's 「(B)」 (2026-10-30: SPEC 15.6's whole-holder text stays, 15.6.4's `reset()` example corrected, BU-67, G303 and U2 closed); U0′ (BU-70); SPEC 4.9.6's destruction lines (a48ba8ddb).
<a id="g270"></a>
- **G270 and G269 (B) (R4, 2026-10-28; DECISIONS R4-M152):** a call carries the Closure receiver's well-formedness premise (each enclosing anonymous function's receiver input), and a Closure called directly retains its environment into its captured exclusive referents on the MIR; the old path's half was G75 E7.
<a id="g272"></a>
- **G272 (R4, 2026-10-29; DECISIONS R4-M153):** A5's past-bound answers are one walk outcome with one join (`MirOutcome.Join`); an exceeded depth bound is the Resource record `OwnershipDepthLimit_Kd` (the owner's 「G272: (1)」). Review 4's N2, N3 and N5 stay in PLAN.
<a id="r5-history"></a>
- **R5 and R6-gen, PLAN §3 to 2026-11-02 (moved by the owner's 「PLAN.mdを整理してください」):** paused and resumed by the owner seven times from 2026-10-23 (handoffs `artifacts/handoff/2026-10-2*-r5-pause/` to `2026-11-01-r5-pause/`); C6–C10, X1–X3 (exit), W1's stages A and B and R6-gen's G0–G11 (exit) are DECISIONS R5-M8 and R5-M110–R5-M169; its unit file holds were released at their landings.
<a id="r4-history"></a>
- **R4, PLAN §3 to 2026-11-02 (moved by the same order):** paused and resumed by the owner seven times from 2026-10-23 (handoffs `artifacts/handoff/2026-10-2*-r4-pause/` to `2026-11-01-r4-pause/`); the units landed meanwhile are DECISIONS R4-M128–R4-M160; its standing rules: concurrency R4-M119, the OldRule ratchet R4-M136, V16 (work beyond input + output size is a defect).

Other closed rows that code, tests or documents cite, with the DECISIONS entries that hold them (closed 2026-11-01 to 2026-11-04):

- **G86:** a conditional slot's Loan requirement judged per admitted Semantics case (BU-44).
- **G122:** an unreadable literal is published Error by `Binding.Rejected` alone (R4-M61).
- **G161:** a raw Place borrow's Loan is its fresh anchor's (R4-M156).
- **G175:** `TestDiagnostics.Text` is the one by-path readout of a diagnostic snapshot (R5, batch 79).
- **G182:** O2 growth under MIR code generation (R5-M84, R5-M104; `draft/Sketches/R5/G182-design.md`).
- **G237:** language-server locations name documents as the client spelled them (R5-M180).
- **G240:** storage preparation publishes `StorageOutcome` (BU-36).
- **G243:** the hold walk's memo per output, no 2^n chains (BU-35, BU-55).
- **G244:** a requirement Item's Components layout (BU-34's note).
- **G245, G247:** the MIR builder over input Binding rejected (R4-M127, R4-M139, R4-M149, BU-37).
- **G266:** rule A's hold walk starts at every position of input j (BU-52, BU-67).
- **G271:** `Binding.KeptType`, the one producer of a stored part's Type (BU-50).
- **G273:** a Closure Type carries enclosing Closures' Origin atoms (R4-M159).
- **G276, G279:** retention per storage; callables a contract hands its body retain as erased callees (R4-M154).
- **G278:** no defect: a projection-typed witness is `UnprovenConstraint_Kd` (BU, bu58).
- **G281:** a body reads a stored part in its own scope (BU-50).
- **G282:** `Binding.RegisterConformance` registers paths by root path (BU, bu56).
- **G290:** a bound Contract reference names a required one by its substituted form (BU, bu58).
- **G292:** a property update beside a custom accessor reads its standard sides (BU-50).
- **G296:** `BindPropertyUpdate` publishes its destination (BU, bu62 and bu80); the remainder closed without a commit after its pinned timing (2026-11-04).
- **G297:** a raw input's own value held at an abstract part (BU-66).
- **G301:** an instance accessor's body is a derived instance body for base calls (BU-53).
- **G305:** A5's backward liveness is the transpose of its forward phases (R4-M158).
- **G306:** independent Origin errors beside a failed clause stay visible (BU-68, BU-69).
- **G307:** A5's Type walks visit a DAG once per Type (R4-M157, BU-58).
- **G310:** SPEC 6.2's finite inline storage judged by Tarjan over per-node summaries (BU-65).
- **G312:** a failed written Origin operand is one record (BU, bu79; `DeclaredOriginSlotTest`).
- **G330:** a code generation exception ends the Emission run located (R5-M169; `draft/Sketches/R5/G330-design.md`).
- **G332:** the collector limits instantiation growth, not depth (R5-M170, BU-71).
- **G336:** LLVM_OPTIMIZATION §4 names the MIR code generation (R5, batch 78).
- **G342:** A5 keeps a store's value past storage the body does not own (R4-M160).
- **G346:** a NUL in a source path no longer faults a command (R5-M169, R5-M175).
- **G347:** every test allocation window goes through `AllocationMeasurement` (R5, batch 75).
- **G350:** cold scaling tests alone (R5-M172; `draft/Sketches/R5/G350-design.md`).
- **G357:** Debug-only warm `Measure` failures from `RuntimeHelpers.CreateSpan` (R5-M179).
- **G358, G366, G371:** runtime texts expanded in one ordered scan through `LlvmModuleText.ExpandRuntime` (R5-M172).
- **G359:** the allocation-context phantom cannot fail a cold scaling series alone (closed by the owner, 2026-11-02).
- **G363:** `IrTarget` publishes the pointer width only (R5-M174).
- **G364:** sources parsed in SPEC 6.2.1's logical declaration order (R5-M176).
- **G367:** each independent configuration failure is its own record (R5-M177).
- **G370:** the built-in Conditions `os` and `arch` follow IMPL 20.2.1 (R5-M178).
- **G372:** every Function value's drop glue is one item (R5-M60).

## Milestone and track records

<a id="arithmetic-completion"></a>
- **2026-10-08 — Arithmetic A0–A5 and P43 complete:** declaration/provider selection, literal fitting, common requirement Items, acquisition and replacement paths (Session `20261007-231931-542-session-arithmetic-reviewed-final`; conditions in [Arithmetic.md](../../src/Benchmark/Arithmetic.md)). G78/G83, exclusive OCC, CSP and native i128/u128 division remained separate; required behavior is in [arithmetic Contracts](../spec/13-operators-and-assignment.md).
<a id="hover-verification"></a>
- **2026-10-07/08 — Hover and improvements:** Session `20261006-173140-848-session-hover-final` and LSP/extension client checks (`artifacts/verify/hover-client-20261007/`); the Virtual Session `20261007-183827-283-session-virtual-remaining-session-repaired` closed the automated-test gap. Actual-client visual review stays user-owned. Policy and measurements: [HOVER.md](HOVER.md).
<a id="documentation-markdown-product-switch-20260920"></a>
- **2026-09-20 — Documentation Markdown product switch:** the record is in the [2026-10-08 snapshot](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/PLAN_HISTORY.md) (its archive L297).

## Retired audit records

<a id="diagnostics-checkpoints"></a>
- **2026-09-30–10-03 — Diagnostics D0–D5 / R1–R7:** D4's 80-case and D5's 202-case corpora, then structured repairs, check JSON and LSP quick fixes (final Repair Session `20261002-174643-505-session-repair-track-session`); dated checkpoints, not conformance certificates. Details: the [archived evaluations](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/DIAGNOSTICS.md#9-evaluation).
<a id="diagnostics-audit"></a>
- **2026-09-30 — Diagnostic conformance audit:** prerequisite aggregation, source order, catalog/fault validation, duplicate evidence and bounded excerpts repaired (second review `f4a9c43b`); the [full audit](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/DIAGNOSTICS_REVIEW.md) keeps the matrices and failed runs. Residual findings: [DIAGNOSTICS §11](DIAGNOSTICS.md#11-audit-findings-d1).
<a id="ranges-audit"></a>
- **2026-09-30 — Ranges audit complete:** `ae55e615` completed the proposal/specification audit and diagnostic repairs, `d7ba81f5` / `f4a9c43b` the second-review corrections; the [full audit](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/RANGES_REVIEW.md) keeps the scope matrix. Its four residual findings are in [DIAGNOSTICS §11](DIAGNOSTICS.md#11-audit-findings-d1).

Everything else: `git show 4fb6fcdb1:docs/dev/PLAN_HISTORY.md` (the last full text, 2026-11-05), `git show 2834c7bf4:docs/dev/PLAN_HISTORY.md` (2026-10-08) and `git show 32324537:PLAN_HISTORY.md` (before 2026-09-22).
