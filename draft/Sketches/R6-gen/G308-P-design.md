# G308 candidate P: a compared composite part is one call per distinct Type

Prototype-first candidate (WORKFLOW "Full changes" step 3), written after the implementation on `claude/r6-g308-p` from the shared
tests `2bb98839d`. It covers Design step 3; the DECISIONS draft is at the end.

## Required meaning

SPEC 13.4 and 13.4.1 fix what a comparison computes (elementwise equality, lexicographic ordering left to right until decided, an
unordered pair false for the four orderings, IEEE for a written float under source operators, the Contract mapping for a `T` leaf and
for `Equatable.equals` / `Comparable.compare` on a Tuple, inspection Loans, no move or drop). SPEC 8.11.3 makes a comparison run its
leaves' witnesses. SPEC 8.15.3 makes reading parts past an indirection a layout use at the requesting site. V16 (AGENTS "fast
compilation") requires every phase linear in the input: for a comparison, the distinct composite Types of the compared Type, not its
root-to-leaf paths. On the DAG family `P_n` the Tuple has 2^n leaves and n distinct composite Types.

## What the representation lacked

`TyCtxt.Comparison` already composes one node per (Contract, Type, mode), so its tree is a DAG over the distinct Types. No node could
be used as a unit, so every reader walked the DAG per root-to-leaf path:

| Reader | Per-path walk | Effect on `P_n` |
| --- | --- | --- |
| `MirBuilder.Expand` / `Leaves` | one MIR leaf per path | MIR 2^n; A1's arena limit at n = 16 (S1-S3) |
| `FunctionCodegen.Shims.Compare` | the whole tree inlined into one shim | IR 2^n (S5, Contract mapping) |
| `InstanceCollector.Leaves` | the tree walked per path (Work does not count it) | time 2^n |
| A4 `Solve.Require` through `Binding.Witnesses` | the legacy plan walked per path | time 2^n (S4's and S6's programs) |

Two facts were missing: whether a node compares as its Contract's mapping (under source operators, no float compared by IEEE and no
numeric Type only the operators compare natively at or below it), and the call that compares a composite node whole (the Contract's
requirement on its Type). With both, a Tuple part that compares as the mapping is the same call wherever it occurs, and every reader can
stop at it.

## Owning component and mechanism

`TyCtxt.Comparison` (the owner of "how a comparison runs on a Type in MIR terms") publishes the two facts: `ComparisonNode.Mapped` and,
on a Composite node, `Target`/`Subst` = the Contract's own requirement on its Type (`Synthesized`, the form a Requirement leaf already
has). Each reader then reads one level:

- **MIR (`MirBuilder.Expand`)**: the operator's root is expanded as before, and so are a reference layer (one part: following it does
  not multiply) and a part that is not `Mapped` (its floats compare by IEEE); a `Mapped` Tuple part is a leaf compared whole by a
  `Requirement` callee on its Type, the existing form of a Requirement leaf (`requirement equals/compare of Equatable/Comparable`),
  decided as any Contract leaf (the bool; the i32 against 0). No new MIR vocabulary; the inspection Loans, the left operand's pending
  view and short-circuit order are unchanged.
- **A4 (`MirEffects`)**: a Requirement callee on a composite Self of a comparison Contract (`Comparison`) reads its leaves by node
  (`Leaves`/`Walk`, each distinct node once): a witness is an edge as its Fn call was, a leaf with an abstract part is judged by the
  caller's premises as its requirement call was (SPEC 8.11.3). `Solve.Require` on a composite Type reads the same walk (a witness entered,
  a requirement on a leaf required), replacing `Binding.Witnesses`' legacy plan walk for composites.
- **Collector (`InstanceCollector.Leaves`)**: a comparison shim's targets are its node's direct parts, a reference layer followed: a
  Witness leaf's body, a Tuple part's own shim (`Runs`: the Builtin item of that Type). Each shim lays out its own Self at its
  admission (`AdmitType`), so a Tuple past a reference layer is a use at the site that admits its shim; the past-storage bit is gone.
- **Codegen (`FunctionCodegen.Shims`)**: a part is a Builtin leaf inline, a reference layer's referent loaded, or the call of the shim's
  next target through `Witness`, the one target rule (a body or a shim), so a shim's text is linear in its Self's parts and the module has
  one shim per distinct Tuple.

## Sites sharing the cause

The four readers above (all fixed). Also per path, outside this candidate: Binding's old effect walk (`Binding.EffectBounds`'
`Comparison`, H5, legacy pipeline only) and the old generator (`LlvmModuleWriter.Comparisons`, frozen, G7); both are deleted by R6.
`Binding.ComparisonProof` is memoized per Type (BU-19) and the legacy plan's construction per (site, Type): not per path.

## Options weighed

1. Inside the readers only (memoize or deduplicate each walk): fixes the collector and A4, but MIR and the shim's IR cannot share
   inline code without a callable unit: rejected.
2. The whole tuple operator as one call of an operator-mode shim (a Builtin key variant for IEEE, ordering's unordered pair mapped by
   the relation's direction, a callee mode in MIR): handles float DAGs too, but adds vocabulary (owner approval) and still cannot key an
   open part with a concrete float (`(T, f64)` at `T = f64` must stay NaN-reflexive at `T` only, N3): deferred (open issue 1).
3. MIR comparison bodies per distinct Type: `MirBodyKind.Shim` bodies were rejected (R5-M126, PD3) and would carry the template's
   parameters: rejected.
4. Chosen: expand only reference layers and parts that are not `Mapped`; every `Mapped` Tuple part is the existing Requirement call (its
   Contract mapping); the backend's shim per distinct Tuple calls its Tuple parts' shims.
5. Within 4, a reference layer as its own call (the first prototype: one rule, "every composite part is a call") against following it:
   following chosen. A layer has one part, so following it is linear; as a call it turned a generic `(a, 1) == (b, 1)` at `T = Box` from
   the witness call into a shim per `ref/Box` (`InstanceCollectorTest`'s shim targets), one more item and call per reference Type for
   nothing shared.

## Departures

- R5-M75 ("the leaves are Places below them, depth first"): the expansion now stops at a Tuple part that compares as its mapping.
  The reasons R5-M75 kept expansion still hold where they apply: IEEE parts are still expanded, and A4 no longer meets a composite's
  Limit because it reads the comparison by node.
- R5-M126 (a shim inlines a Tuple's parts and a ref layer's referent; rejected "one function per tree node"): a Tuple part is now a
  call of its own shim (a ref layer stays inline). The rejection's reason (more functions and calls) does not hold against per-path
  inlining, exponential on a DAG; one function per distinct Tuple is linear and O2 inlines the calls.
- R5-M149: the comparison node's `LayoutNeeds` row goes (the closed vocabulary `LayoutNeed` is unchanged: `Parts` stays for projections);
  the use it recorded is now the part shim's admission, with the same records at the same site.
- No new vocabulary entry, no owner question beyond the choice's (K-package 4.3's Builtin key is unchanged).

## Deletions

`LayoutNeeds.Of(ComparisonNode)` and `TypeOf(ComparisonNode)`, `FunctionCodegen.Needed(ComparisonNode)`, the collector's past-storage
parameter and its `Layout.Indirect` test, the shim writer's recursion into Tuple parts, A4's reading of the legacy plan for a composite.

## Size and speed

- Size (numstat against `2bb98839d`, comments included): src +200 / -112, net +88: MIR analyses (`MirEffects`, `MirEffects.Solve`) +85,
  codegen (`FunctionCodegen.Shims`, `.Values`) +8, MIR builder +5, TyCtxt +6, collector -4, `LayoutNeeds` -12; tests: the retargeted
  collector test 0, goldens +41 (`tuples.kimi`'s `parts` row and its MIR).
- Work on `P_n` (n = 8, 12, 16): total MIR 9,868, 9,944, 10,020 for `==` (on dev 11,377, 34,493, 403,209), 9,871, 9,947, 10,023 for `<`
  and `>=`: 19 statements and blocks per level, the rest the library's bodies; the collector's items and Work 11, 15, 19 (S4) and items
  25, 29, 33, Work 79, 83, 87 (S6): one shim per level; A1-A6 have no problem at n = 16 and 20 (S3); the module's IR text and function
  count pass S5's ratio rule for the operator and the Contract mapping.
- `check` with the built `Kimi.dll` (one run each, machine loaded 82-99 % by other sessions' test runs): `P_16`, `P_20`, `P_24` 2.54,
  2.48, 2.47 s (default pipeline) and 2.33, 2.55, 2.36 s (`KIMI_PIPELINE=mir`); S4's and S6's programs at n = 24: 2.88 and 2.67 s
  (default), 2.75 and 3.03 s (mir): flat, the process's fixed cost (on dev `P_24` took 28.5 s).
- Generated code: a Mapped Tuple part is one shim call at O0 (inlined at O2); a flat Tuple (no Tuple part) is unchanged. The O2 tuple
  kernel A/B is not measured (a completion check of the chosen candidate).

## Open issues

1. A DAG whose every level holds a float compared by IEEE (source operators) is still expanded per path in MIR. Proposal: option 2 for a
   closed part that is not `Mapped`; an open one needs a per-instance function keyed by its template node.
2. The legacy pipeline's old effect walk (H5) and the old generator (G7) stay per path until R6 deletes them.
3. `Binding.Witnesses`' comparison-plan walk is now reached only for a non-composite Self (no parts); its removal is staged with A-12/H11.
4. A by-value composite part's shim lays out its Self at admission; where that layout fails, its storage already failed. Whether a
   second record appears at the comparison site is unverified (no diagnostic snapshot run).

## DECISIONS entry draft

- **R5-M164 (provisional) A compared composite part is one call per distinct Type (G308)** (2026-10-31, R5; PLAN G308, Full change,
  candidate P; under R5-M68, R5-M75, R5-M126, R5-M149; V16) `TyCtxt.Comparison`'s nodes form a DAG over the distinct Types; each now
  publishes `Mapped` (it compares as the Contract's mapping: under source operators no float or operator-only numeric leaf below it) and
  a Composite node names the Contract's requirement on its Type (`Target`, `Subst`), the call that compares it whole. Every reader reads
  one level, a reference layer followed: a tuple operator expands its root and its parts that are not Mapped and compares every Mapped
  Tuple part by that Requirement call (R5-M75's leaf form); A4 reads a composite comparison's leaves by node (a witness as its call, an
  abstract leaf by the caller's premises; `Solve.Require` the same, replacing the legacy plan walk); a comparison shim's targets are its
  direct parts' (a witness body, a Tuple part's own shim), and its function calls them, so the module has one shim per distinct Tuple,
  each laying out its Self at admission (R5-M149's comparison row deleted). Supersedes R5-M126's inlining of a Tuple's parts and its
  rejected "one function per tree node" for Tuple parts. Rejected: deduplicating walks only (MIR and IR stay exponential); an
  operator-mode shim now (vocabulary, and an open part that is not Mapped cannot be keyed by a closed Type); MIR comparison bodies (PD3);
  a reference layer as its own shim (nothing shared, one more call). Open: DAGs of IEEE floats, H5's and G7's per-path walks until R6.
